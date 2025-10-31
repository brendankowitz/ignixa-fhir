// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Autofac.Extensions.DependencyInjection;
using Medino;
using Microsoft.IO;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Ignixa.Api.Infrastructure;
using Ignixa.Api.Middleware;
using Ignixa.Api.Services;
using Ignixa.Api.Features.Compartment;
using Ignixa.Api.Features.Metadata.Api;
using Ignixa.Application.Features;
using Ignixa.Domain.Abstractions;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.DataLayer.InMemoryIndex;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.Resource;
using Ignixa.Search.Parsing;
using Ignixa.Search.Definition;
using Ignixa.Specification;
using Ignixa.Domain;
// using Ignixa.Validation.SourceNodeValidation; // Removed - migrating to new FastValidator in Phase 3
using Ignixa.Application.Infrastructure;
using Ignixa.Search.Infrastructure;
using Ignixa.Application.Infrastructure.Behaviors;
using Ignixa.Domain.Models;
using Ignixa.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Configure Autofac as the service provider factory
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// Add services to the container
// Note: Using Minimal API endpoints, not MVC controllers
builder.Services.AddOpenApi();

// Register MemoryCache for CapabilityStatement caching (Phase 1.2)
builder.Services.AddMemoryCache();

// Register RecyclableMemoryStreamManager as singleton
builder.Services.AddSingleton<RecyclableMemoryStreamManager>();

// Configure ForwardedHeaders for Docker/container deployments (supports reverse proxies like Azure App Service)
// Enables X-Forwarded-Host and X-Forwarded-Prefix headers for correct URL generation behind proxies
if (string.Equals(builder.Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        // Default value for options.ForwardedHeaders is ForwardedHeaders.None.
        options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedPrefix;

        // Only loopback proxies are allowed by default.
        // Clear that restriction because forwarders are enabled by explicit configuration.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// Configure Host Filtering for Azure App Service and container deployments
// App Service sends requests via internal hostnames which would be rejected without this configuration
builder.Services.Configure<HostFilteringOptions>(options =>
{
    var allowedHosts = builder.Configuration["AllowedHosts"]?.Split(";") ?? ["*"];
    foreach (var host in allowedHosts)
    {
        options.AllowedHosts.Add(host);
    }
});

// Configure blob storage options
builder.Services.Configure<Ignixa.DataLayer.BlobStorage.Infrastructure.LocalFileBlobStorageOptions>(
    builder.Configuration.GetSection("LocalFileBlobStorage"));

// Register IHttpContextFactory and IHttpContextAccessor for bundle entry pipeline routing
builder.Services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();
builder.Services.AddHttpContextAccessor();

// Register IndexLoaderService as hosted service
builder.Services.AddHostedService<IndexLoaderService>();

// Register HTTP client factory for background operations (Import activities need this)
builder.Services.AddHttpClient();

// Register DurableTask framework for background job processing ($export, $import)
builder.Services.AddDurableTask();

// Register Export activities for dependency injection
builder.Services.AddTransient<Ignixa.Application.BackgroundOperations.Export.Activities.SearchAndWriteChunkActivity>();
builder.Services.AddTransient<Ignixa.Application.BackgroundOperations.Export.Activities.CompleteJobActivity>();

// Register Import activities for dependency injection
builder.Services.AddTransient<Ignixa.Application.BackgroundOperations.Import.Activities.ValidateFileActivity>();
builder.Services.AddTransient<Ignixa.Application.BackgroundOperations.Import.Activities.DownloadAndParseActivity>();
builder.Services.AddTransient<Ignixa.Application.BackgroundOperations.Import.Activities.ImportBatchActivity>();
builder.Services.AddTransient<Ignixa.Application.BackgroundOperations.Import.Activities.UpdateProgressActivity>();
builder.Services.AddTransient<Ignixa.Application.BackgroundOperations.Import.Activities.CompleteJobActivity>();

// Configure Autofac container
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    // Register InMemoryResourceLocationIndex
    containerBuilder.RegisterType<InMemoryResourceLocationIndex>()
        .As<IResourceLocationIndex>()
        .SingleInstance();

    // MULTI-TENANCY CONFIGURATION (Phase 20 - ADR-2523)

    // Register ITenantConfigurationStore (loads tenant configurations from appsettings.json)
    containerBuilder.RegisterType<AppSettingsTenantConfigurationStore>()
        .As<ITenantConfigurationStore>()
        .SingleInstance();

    // Register individual factory implementations with Named registrations
    // These are consumed by CompositeRepositoryFactory and CompositeSearchServiceFactory
    // Named registrations prevent circular dependency with composite factories
    containerBuilder.RegisterType<FileBasedFhirRepositoryFactory>()
        .Named<IFhirRepositoryFactory>("FileSystem")
        .SingleInstance();

    containerBuilder.RegisterType<FileBasedSearchServiceFactory>()
        .Named<ISearchServiceFactory>("FileSystem")
        .SingleInstance();

    // SqlEntityFrameworkRepositoryFactory implements both interfaces, register with both names
    containerBuilder.RegisterType<SqlEntityFrameworkRepositoryFactory>()
        .Named<IFhirRepositoryFactory>("SqlEf")
        .Named<ISearchServiceFactory>("SqlEf")
        .SingleInstance();

    // Register composite factories as main interfaces
    // Route requests to appropriate storage provider based on tenant configuration
    // Use lambda registration to resolve named dependencies explicitly
    containerBuilder.Register<IFhirRepositoryFactory>(c =>
        new CompositeRepositoryFactory(
            c.Resolve<ITenantConfigurationStore>(),
            c.ResolveNamed<IFhirRepositoryFactory>("FileSystem"),
            c.ResolveNamed<IFhirRepositoryFactory>("SqlEf")))
        .SingleInstance();

    containerBuilder.Register<ISearchServiceFactory>(c =>
        new CompositeSearchServiceFactory(
            c.Resolve<ITenantConfigurationStore>(),
            c.ResolveNamed<ISearchServiceFactory>("FileSystem"),
            c.ResolveNamed<ISearchServiceFactory>("SqlEf")))
        .SingleInstance();

    // Register IPartitionStrategy based on configured TenantMode (Phase 20 - ADR-2523)
    // Factory pattern allows strategy selection at startup based on appsettings.json Tenants:Mode
    // - Isolated: Single partition per tenant (multi-tenant SaaS with different customers)
    // - Distributed: Horizontal sharding for scale (single customer, not yet implemented)
    containerBuilder.Register<IPartitionStrategy>(c =>
    {
        var configStore = c.Resolve<ITenantConfigurationStore>();
        var loggerFactory = c.Resolve<ILoggerFactory>();

        return configStore.Mode switch
        {
            TenantMode.Isolated => new IsolatedModePartitionStrategy(
                loggerFactory.CreateLogger<IsolatedModePartitionStrategy>()),
            TenantMode.Distributed => throw new NotSupportedException(
                "Distributed mode is not yet implemented (Phase 20.2+). " +
                "Set Tenants:Mode to 'Isolated' in appsettings.json."),
            _ => throw new InvalidOperationException(
                $"Unknown TenantMode: {configStore.Mode}. Valid values: Isolated, Distributed")
        };
    }).As<IPartitionStrategy>().SingleInstance();

    // Register IQueryExecutionStrategy based on configured TenantMode (Phase 20 - ADR-2523)
    // Factory pattern allows strategy selection at startup based on appsettings.json Tenants:Mode
    // - Isolated: Passthrough (validates single partition, streams directly)
    // - Distributed: Fanout (parallel queries to multiple shards, not yet implemented)
    containerBuilder.Register<IQueryExecutionStrategy>(c =>
    {
        var configStore = c.Resolve<ITenantConfigurationStore>();
        var searchServiceFactory = c.Resolve<ISearchServiceFactory>();
        var loggerFactory = c.Resolve<ILoggerFactory>();

        return configStore.Mode switch
        {
            TenantMode.Isolated => new PassthroughExecutionStrategy(
                searchServiceFactory,
                loggerFactory.CreateLogger<PassthroughExecutionStrategy>()),
            TenantMode.Distributed => throw new NotSupportedException(
                "Distributed mode is not yet implemented (Phase 20.2+). " +
                "Set Tenants:Mode to 'Isolated' in appsettings.json."),
            _ => throw new InvalidOperationException(
                $"Unknown TenantMode: {configStore.Mode}. Valid values: Isolated, Distributed")
        };
    }).As<IQueryExecutionStrategy>().SingleInstance();

    // Register IAuditLogger (logs tenant access for security/compliance)
    containerBuilder.RegisterType<AuditLogger>()
        .As<IAuditLogger>()
        .SingleInstance();

    // BULK EXPORT INFRASTRUCTURE (Phase 13 - ADR-2516)

    // Register blob storage client factory
    // Supports provider-based selection: Local (filesystem) or Azure (Blob Storage)
    // Provider chosen via configuration: BlobStorage:Provider = "Local" | "Azure"
    containerBuilder.Register(c =>
    {
        var configuration = c.Resolve<IConfiguration>();
        var factory = new Ignixa.DataLayer.BlobStorage.Infrastructure.BlobClientFactory(
            configuration,
            c.Resolve<IComponentContext>().Resolve<IServiceProvider>(),
            c.Resolve<ILogger<Ignixa.DataLayer.BlobStorage.Infrastructure.BlobClientFactory>>());
        // Use GetAwaiter().GetResult() to make async factory work in sync Autofac context
        return factory.CreateClientAsync().GetAwaiter().GetResult();
    })
    .As<IBlobStorageClient>()
    .SingleInstance();

    // Register export job store (in-memory for prototype, SQL Server for production)
    containerBuilder.RegisterType<Ignixa.DataLayer.BlobStorage.Features.Export.InMemoryExportJobStore>()
        .As<IExportJobStore>()
        .SingleInstance();

    // Register import job store (in-memory for prototype, SQL Server for production)
    containerBuilder.RegisterType<Ignixa.DataLayer.BlobStorage.Features.Import.InMemoryImportJobStore>()
        .As<IImportJobStore>()
        .SingleInstance();

    // Register Medino service provider
    containerBuilder.Register<IMediatorServiceProvider>(c =>
    {
        var context = c.Resolve<IComponentContext>();
        return new AutofacMediatorServiceProvider(context);
    }).SingleInstance();

    // Register Medino mediator
    containerBuilder.RegisterType<Mediator>().As<IMediator>().SingleInstance();

    // CRITICAL: Register pipeline behaviors BEFORE handlers
    // Medino resolves behaviors as IEnumerable<IPipelineBehavior<TRequest, TResponse>>
    // Order matters: CapabilityEnforcementBehavior runs first, then ValidationBehavior

    // Generic resource handlers (replaces Patient-specific handlers)
    containerBuilder.RegisterType<GetResourceHandler>()
        .As<IRequestHandler<GetResourceQuery, SearchEntryResult?>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<CreateOrUpdateResourceHandler>()
        .As<IRequestHandler<CreateOrUpdateResourceCommand, Ignixa.Domain.Models.UpdateResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<DeleteResourceHandler>()
        .As<IRequestHandler<DeleteResourceCommand, bool>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<SearchResourcesHandler>()
        .As<IRequestHandler<SearchResourcesQuery, SearchResourcesResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.Compartment.SearchCompartmentHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.Compartment.SearchCompartmentQuery, SearchResourcesResult>>()
        .InstancePerDependency();

    // Conditional Operations (Phase 23 - ADR-2525: FHIR Conditional Operations)
    containerBuilder.RegisterType<Ignixa.Application.Features.ConditionalOperations.ConditionalCreate.ConditionalCreateHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.ConditionalOperations.ConditionalCreate.ConditionalCreateCommand, Ignixa.Application.Features.ConditionalOperations.ConditionalCreate.ConditionalCreateResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate.ConditionalUpdateHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate.ConditionalUpdateCommand, Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate.ConditionalUpdateResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.ConditionalOperations.ConditionalDelete.ConditionalDeleteHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.ConditionalOperations.ConditionalDelete.ConditionalDeleteCommand, Ignixa.Application.Features.ConditionalOperations.ConditionalDelete.ConditionalDeleteResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.ConditionalOperations.ConditionalPatch.ConditionalPatchHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.ConditionalOperations.ConditionalPatch.ConditionalPatchCommand, Ignixa.Application.Features.ConditionalOperations.ConditionalPatch.ConditionalPatchResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.ConditionalOperations.ConditionalRead.ConditionalReadHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.ConditionalOperations.ConditionalRead.ConditionalReadQuery, Ignixa.Application.Features.ConditionalOperations.ConditionalRead.ConditionalReadResult>>()
        .InstancePerDependency();

    // History handlers (Phase 22 - ADR-2524: FHIR _history operations)
    // NOW WITH STREAMING: Returns HistoryResult with IAsyncEnumerable<SearchEntryResult> for efficient memory usage
    containerBuilder.RegisterType<Ignixa.Application.Features.History.GetResourceHistoryHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.History.GetResourceHistoryQuery, Ignixa.Application.Features.History.HistoryResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.History.GetTypeHistoryHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.History.GetTypeHistoryQuery, Ignixa.Application.Features.History.HistoryResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.History.GetSystemHistoryHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.History.GetSystemHistoryQuery, Ignixa.Application.Features.History.HistoryResult>>()
        .InstancePerDependency();

    // Patch handlers (Phase 17 - ADR-2520: FHIR Patch operations)
    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.PatchResourceHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.Patch.PatchResourceCommand, ResourceWrapper?>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.FhirPatchParametersParser>()
        .AsSelf()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.FhirPatchEngine>()
        .AsSelf()
        .InstancePerDependency();

    // Patch validators (Phase 4 - Validation Framework)
    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.Validation.FhirPatchValidator>()
        .AsSelf()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.Validation.ImmutablePropertyValidator>()
        .AsSelf()
        .InstancePerDependency();

    // FHIRPath dependencies for PATCH operations
    containerBuilder.RegisterType<Ignixa.FhirPath.Evaluation.FhirPathEvaluator>()
        .AsSelf()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.FhirPath.FhirPathCompiler>()
        .AsSelf()
        .InstancePerDependency();

    // FhirPathPatchHelper with structure provider from FhirVersionContext
    containerBuilder.Register<Ignixa.Application.Features.Patch.FhirPathPatchHelper>(c =>
    {
        var evaluator = c.Resolve<Ignixa.FhirPath.Evaluation.FhirPathEvaluator>();
        var compiler = c.Resolve<Ignixa.FhirPath.FhirPathCompiler>();
        var versionContext = c.Resolve<IFhirVersionContext>();

        // Use R4 as default structure provider (Phase 1)
        // TODO Phase 2+: Extract version from tenant context
        var structureProvider = versionContext.GetSchemaProvider(FhirSpecification.R4);

        return new Ignixa.Application.Features.Patch.FhirPathPatchHelper(
            evaluator,
            compiler,
            structureProvider);
    })
    .AsSelf()
    .InstancePerDependency();

    // Patch operation executors (Phase 2 - Strategy Pattern)
    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.Executors.AddOperationExecutor>()
        .As<Ignixa.Application.Features.Patch.Executors.IOperationExecutor>()
        .AsSelf()
        .InstancePerLifetimeScope();

    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.Executors.InsertOperationExecutor>()
        .As<Ignixa.Application.Features.Patch.Executors.IOperationExecutor>()
        .AsSelf()
        .InstancePerLifetimeScope();

    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.Executors.DeleteOperationExecutor>()
        .As<Ignixa.Application.Features.Patch.Executors.IOperationExecutor>()
        .AsSelf()
        .InstancePerLifetimeScope();

    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.Executors.ReplaceOperationExecutor>()
        .As<Ignixa.Application.Features.Patch.Executors.IOperationExecutor>()
        .AsSelf()
        .InstancePerLifetimeScope();

    containerBuilder.RegisterType<Ignixa.Application.Features.Patch.Executors.MoveOperationExecutor>()
        .As<Ignixa.Application.Features.Patch.Executors.IOperationExecutor>()
        .AsSelf()
        .InstancePerLifetimeScope();

    // NOTE: FileBasedSearchService is no longer registered as singleton
    // It is now created per tenant by FileBasedSearchServiceFactory

    // Register query parameter parser
    containerBuilder.RegisterType<QueryParameterParser>()
        .As<IQueryParameterParser>()
        .InstancePerDependency();

    // Register FhirVersionContext (provides version-specific schema providers, search indexers, etc.)
    // Similar to HAPI FHIR's FhirContext pattern - caches instances per FHIR version
    // Moved to Ignixa.Search layer for proper dependency management
    containerBuilder.RegisterType<FhirVersionContext>()
        .As<IFhirVersionContext>()
        .SingleInstance();

    // Register SearchOptionsBuilderFactory for version-aware search options builders
    // Factory creates and caches builders per (tenant, FHIR version) pair
    // Phase 1: Single-tenant mode (uses TenantContext.Default)
    // Phase 2+: Multi-tenant mode with custom search parameters per tenant
    // Now uses IFhirVersionContext to reuse cached managers
    containerBuilder.RegisterType<SearchOptionsBuilderFactory>()
        .As<ISearchOptionsBuilderFactory>()
        .SingleInstance();

    // Register FhirSchemaProviderResolver - enables version-aware components to resolve
    // the correct provider at runtime based on request FHIR version
    containerBuilder.Register<FhirSchemaProviderResolver>(c =>
    {
        var versionContext = c.Resolve<IFhirVersionContext>();
        return (FhirSpecification version) => versionContext.GetSchemaProvider(version);
    }).SingleInstance();

    // DEPRECATED: VersionAwareSearchParameterDefinitionManager replaced by FhirVersionContext.GetSearchParameterDefinitionManager()
    // Multi-version support now provided via FhirVersionContext pattern (same as SearchIndexer and SchemaProvider)
    // For single-tenant mode (Phase 1), default to R4 for backward compatibility
    containerBuilder.Register<ISearchParameterDefinitionManager>(c =>
    {
        var versionContext = c.Resolve<IFhirVersionContext>();
        // Single-tenant mode: default to R4 for backward compatibility
        // Multi-tenant mode: will be resolved from tenant-specific FHIR version in request context
        return versionContext.GetSearchParameterDefinitionManager(FhirSpecification.R4);
    }).SingleInstance();

    containerBuilder.Register<ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver>(c =>
    {
        var manager = c.Resolve<ISearchParameterDefinitionManager>();
        return () => manager;
    }).SingleInstance();

    // DEPRECATED: VersionAwareCompartmentDefinitionManager replaced by FhirVersionContext.GetCompartmentDefinitionManager()
    // Register default ICompartmentDefinitionManager as R4 (for legacy code that doesn't specify version)
    containerBuilder.Register<ICompartmentDefinitionManager>(c =>
    {
        var versionContext = c.Resolve<IFhirVersionContext>();
        return versionContext.GetCompartmentDefinitionManager(FhirSpecification.R4);
    }).SingleInstance();

    // NOTE: ReferenceSearchValueParser, SearchParameterExpressionParser, ExpressionParser, and SearchOptionsBuilder
    // are now created by SearchOptionsBuilderFactory with version-specific dependencies
    // No longer registered in DI container - factory creates them per (tenant, version) pair

    // PHASE 1.2: Segmented CapabilityStatement with Smart Caching

    // Register capability cache (Phase 1.2: in-memory, Phase 7: Redis)
    containerBuilder.RegisterType<Ignixa.Application.Infrastructure.Caching.MemoryCapabilityCache>()
        .As<Ignixa.Application.Infrastructure.Caching.ICapabilityCache>()
        .SingleInstance();

    // Register capability segments (ordered by priority)
    containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.Segments.StaticCapabilitySegment>()
        .As<Ignixa.Application.Features.Metadata.Segments.ICapabilitySegment>()
        .SingleInstance();

    containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.Segments.ResourceInteractionCapabilitySegment>()
        .As<Ignixa.Application.Features.Metadata.Segments.ICapabilitySegment>()
        .SingleInstance();

    containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.Segments.SearchParameterCapabilitySegment>()
        .As<Ignixa.Application.Features.Metadata.Segments.ICapabilitySegment>()
        .SingleInstance();

    containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.Segments.IncludeRevIncludeCapabilitySegment>()
        .As<Ignixa.Application.Features.Metadata.Segments.ICapabilitySegment>()
        .SingleInstance();

    containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.Segments.ProfileCapabilitySegment>()
        .As<Ignixa.Application.Features.Metadata.Segments.ICapabilitySegment>()
        .SingleInstance();

    // Register CapabilityStatementService (orchestrates segments + caching)
    containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.CapabilityStatementService>()
        .AsSelf()
        .SingleInstance();

    // PHASE 3: Capability Enforcement with FHIRPath

    // Register Medino pipeline behaviors
    // CRITICAL FIX: For behaviors to work with Medino + Autofac, they must be registered
    // in a way that allows enumeration. Autofac supports this for open generics via RegisterGeneric.
    // The key is using InstancePerLifetimeScope (NOT InstancePerDependency) to ensure proper resolution.

    // Register CapabilityEnforcementBehavior (generic - applies to ALL requests implementing IRequireCapability)
    containerBuilder.RegisterGeneric(typeof(CapabilityEnforcementBehavior<,>))
        .As(typeof(IPipelineBehavior<,>))
        .InstancePerLifetimeScope();

    // Register ValidationBehavior (specific to CreateOrUpdateResourceCommand)
    containerBuilder.RegisterType<ValidationBehavior>()
        .As<IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>>()
        .InstancePerLifetimeScope();

    // Register capability cache invalidator (Phase 3)
    containerBuilder.RegisterType<Ignixa.Application.Infrastructure.Caching.CapabilityCacheInvalidator>()
        .As<Ignixa.Application.Infrastructure.Caching.ICapabilityCacheInvalidator>()
        .SingleInstance();

    // Register GetCapabilityStatementHandler (uses service)
    containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.GetCapabilityStatementHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.Metadata.GetCapabilityStatementQuery, Ignixa.Application.Features.Metadata.Models.CapabilityStatementJsonNode>>()
        .InstancePerDependency();

    // Register FHIR Validation Services (Phase 3 - Tier-aware validation)
    // Uses three-tier validation: Fast (universal checks), Spec (schema checks), Profile (advanced)
    // Tier is configured per-tenant via TenantConfiguration.ValidationTier

    // Register FhirPathCompiler (shared across FhirPath validation and PATCH operations)
    containerBuilder.RegisterType<Ignixa.FhirPath.FhirPathCompiler>()
        .AsSelf()
        .SingleInstance();

    // Register validation schema builder (shared, creates schemas from StructureDefinitions)
    containerBuilder.Register(c =>
        {
            var compiler = c.Resolve<Ignixa.FhirPath.FhirPathCompiler>();
            return new Ignixa.Validation.Schema.StructureDefinitionSchemaBuilder(compiler);
        })
        .AsSelf()
        .SingleInstance();

    // Register ValidationSchemaResolver FACTORY (creates version-specific cached resolvers)
    // Usage: Func<FhirSpecification, IValidationSchemaResolver> factory = resolve from DI
    //        IValidationSchemaResolver resolver = factory(FhirSpecification.R4)
    containerBuilder.Register<Func<FhirSpecification, Ignixa.Validation.Abstractions.IValidationSchemaResolver>>(c =>
        {
            var versionContext = c.Resolve<Ignixa.Search.Infrastructure.IFhirVersionContext>();
            var builder = c.Resolve<Ignixa.Validation.Schema.StructureDefinitionSchemaBuilder>();

            return (version) =>
            {
                var schemaProvider = versionContext.GetSchemaProvider(version);
                var resolver = new Ignixa.Validation.Schema.StructureDefinitionSchemaResolver(schemaProvider, builder);
                return new Ignixa.Validation.Schema.CachedValidationSchemaResolver(resolver);
            };
        })
        .SingleInstance();

    // Register InMemoryTerminologyService (basic terminology validation)
    containerBuilder.RegisterType<Ignixa.Validation.Services.InMemoryTerminologyService>()
        .As<Ignixa.Validation.Abstractions.ITerminologyService>()
        .SingleInstance();

    // Register bundle processing services
    containerBuilder.RegisterType<BundleReferencePreProcessor>()
        .InstancePerDependency();

    containerBuilder.RegisterType<BundleEntryExecutor>()
        .InstancePerDependency();

    containerBuilder.RegisterType<BundleChannelExecutor>()
        .InstancePerDependency();

    containerBuilder.RegisterType<BundleResponseBuilder>()
        .InstancePerDependency();

    containerBuilder.RegisterType<BundleProcessor>()
        .InstancePerDependency();

    // Register StreamingBundleParser for Prefer: streaming header support
    containerBuilder.RegisterType<StreamingBundleParser>()
        .InstancePerDependency();

    // Register pipeline executor for bundle entry routing
    // Uses ASP.NET Core endpoint routing infrastructure (similar to microsoft/fhir-server BundleRouter)
    containerBuilder.Register(c =>
    {
        var endpointDataSource = c.Resolve<EndpointDataSource>();
        var matcherPolicies = c.Resolve<IEnumerable<Microsoft.AspNetCore.Routing.MatcherPolicy>>();
        var endpointSelector = c.Resolve<Microsoft.AspNetCore.Routing.Matching.EndpointSelector>();
        var templateBinderFactory = c.Resolve<Microsoft.AspNetCore.Routing.Template.TemplateBinderFactory>();
        return new AspNetCorePipelineExecutor(endpointDataSource, matcherPolicies, endpointSelector, templateBinderFactory);
    })
    .As<Ignixa.Application.Infrastructure.IPipelineExecutor>()
    .SingleInstance();
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseFhirExceptionHandler();

// Use ForwardedHeaders middleware early in the pipeline for Docker/container deployments
// Processes X-Forwarded-Host and X-Forwarded-Prefix headers if ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
if (string.Equals(builder.Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
{
    app.UseForwardedHeaders();
}

// MULTI-TENANCY MIDDLEWARE (Phase 20 - ADR-2523)
// Extracts tenantId from route, validates tenant exists and is active
// Stores tenant context in HttpContext.Items for downstream handlers
app.UseMiddleware<TenantResolutionMiddleware>();

// CAPABILITY ENFORCEMENT (Phase 3 - ADR-2506)
// Now handled by CapabilityEnforcementBehavior (Medino pipeline behavior)
// Commands/queries implement IRequiresCapability and declare FHIRPath expressions
// Behavior validates requests against CapabilityStatement before executing handlers

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapFhirEndpoints();
app.MapFhirHistoryEndpoints(); // FHIR _history endpoints (instance, type, system-level)
app.MapPatchEndpoints(); // FHIR PATCH endpoints (direct and conditional)
app.MapCompartmentEndpoints(); // FHIR compartment search endpoints (GET /Patient/123/Observation)
app.MapMetadataEndpoints(); // FHIR metadata endpoints (CapabilityStatement)
Ignixa.Api.Features.Export.Api.ExportEndpoints.MapExportEndpoints(app); // Bulk export endpoints (DurableTask)

app.Logger.LogInformation("Ignixa FHIR starting...");
app.Logger.LogInformation("FHIR data directory: {BaseDirectory}",
    builder.Configuration["FhirRepository:BaseDirectory"]
    ?? Path.Combine(Directory.GetCurrentDirectory(), "fhir-data"));

// MULTI-TENANCY STARTUP VALIDATION (Phase 20 - ADR-2523)
// Validate tenant configuration and log mode information
{
    var configStore = app.Services.GetRequiredService<ITenantConfigurationStore>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    // Log current mode
    logger.LogInformation("===== FHIR Server Multi-Tenancy Configuration =====");
    logger.LogInformation(
        "Mode: {Mode} ({Description})",
        configStore.Mode,
        configStore.Mode == TenantMode.Isolated
            ? "Multiple separate customers with isolated data stores"
            : "Single customer with horizontal sharding");

    // Load and log tenant count
    var tenants = await configStore.GetAllTenantsAsync();
    logger.LogInformation("Active Tenants: {Count}", tenants.Count);

    foreach (var tenant in tenants)
    {
        logger.LogInformation(
            "  - Tenant {TenantId}: {DisplayName} (FHIR {FhirVersion}, Storage: {StorageType})",
            tenant.TenantId,
            tenant.DisplayName,
            tenant.FhirVersion,
            tenant.Storage.Type);
    }

    // Warn if Distributed mode configured (not yet supported)
    if (configStore.Mode == TenantMode.Distributed)
    {
        logger.LogWarning(
            "WARNING: Distributed mode is configured but not yet implemented (Phase 20.2+). " +
            "The system will throw NotSupportedException if Distributed features are accessed. " +
            "For production use, set Tenants:Mode to 'Isolated' in appsettings.json.");
    }

    // Validate strategy types match mode
    var partitionStrategy = app.Services.GetRequiredService<IPartitionStrategy>();
    var executionStrategy = app.Services.GetRequiredService<IQueryExecutionStrategy>();

    logger.LogInformation(
        "Registered Strategies: IPartitionStrategy={PartitionStrategy}, IQueryExecutionStrategy={ExecutionStrategy}",
        partitionStrategy.GetType().Name,
        executionStrategy.GetType().Name);

    // Validate Isolated mode configuration
    if (configStore.Mode == TenantMode.Isolated)
    {
        if (partitionStrategy is not IsolatedModePartitionStrategy)
        {
            logger.LogError(
                "Configuration Error: Mode is Isolated but IPartitionStrategy is {ActualType}. " +
                "Expected: IsolatedModePartitionStrategy",
                partitionStrategy.GetType().Name);
            throw new InvalidOperationException(
                "Configuration mismatch: Mode is Isolated but wrong partition strategy registered");
        }

        if (executionStrategy is not PassthroughExecutionStrategy)
        {
            logger.LogError(
                "Configuration Error: Mode is Isolated but IQueryExecutionStrategy is {ActualType}. " +
                "Expected: PassthroughExecutionStrategy",
                executionStrategy.GetType().Name);
            throw new InvalidOperationException(
                "Configuration mismatch: Mode is Isolated but wrong execution strategy registered");
        }

        logger.LogInformation("✅ Isolation mode validation passed");
    }

    logger.LogInformation("===================================================");
}

// CRITICAL: Initialize all tenant databases (applies migrations, creates TVP types)
// This ensures dbo.ResourceListTableType and all 16+ TVP types exist before SqlMergeRepository is used
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var configStore = app.Services.GetRequiredService<ITenantConfigurationStore>();
    var repositoryFactory = app.Services.GetRequiredService<IFhirRepositoryFactory>();

    logger.LogInformation("===== Database Initialization =====");

    var tenants = await configStore.GetAllTenantsAsync();
    foreach (var tenant in tenants)
    {
        try
        {
            logger.LogInformation("Initializing database for tenant {TenantId} ({DisplayName})...", tenant.TenantId, tenant.DisplayName);

            // This will trigger SqlEntityFrameworkRepositoryFactory to create the repository
            // which internally calls DatabaseInitializer.InitializeAsync() to apply migrations
            var repository = await repositoryFactory.GetRepositoryAsync(tenant.TenantId);

            logger.LogInformation("✅ Database initialized for tenant {TenantId}", tenant.TenantId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to initialize database for tenant {TenantId} ({DisplayName}). Error: {Message}",
                tenant.TenantId, tenant.DisplayName, ex.Message);
            throw;
        }
    }

    logger.LogInformation("===== All Databases Initialized =====");
}

await app.RunAsync();
