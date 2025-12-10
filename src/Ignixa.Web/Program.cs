// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Autofac.Extensions.DependencyInjection;
using DurableTask.Core;
using Ignixa.Abstractions;
using Medino;
using Microsoft.IO;
using Polly;
using Polly.Extensions.Http;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Ignixa.Api.Infrastructure;
using Ignixa.Api.Middleware;
using Ignixa.Api.Services;
using Ignixa.Api.Endpoints;
using Ignixa.Application.Features;
using Ignixa.Domain.Abstractions;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.DataLayer.InMemoryIndex;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Search.Parsing;
using Ignixa.Search.Definition;
using Ignixa.Specification;
using Ignixa.Domain;
using Ignixa.Domain.Constants;
// using Ignixa.Validation.SourceNodeValidation; // Removed - migrating to new FastValidator in Phase 3
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Behaviors;
using Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Parser;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.SqlOnFhir;
using Ignixa.SqlOnFhir.packages;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Microsoft.EntityFrameworkCore;
using FhirVersion = Ignixa.Abstractions.FhirVersion;

var builder = WebApplication.CreateBuilder(args);

// STARTUP TIMING DIAGNOSTICS
// Enable via Diagnostics:StartupTiming:Enabled = true (default: true in Development)
builder.Services.AddStartupTimingDiagnostics();

// Configure Autofac as the service provider factory
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// Add services to the container
// Note: Using Minimal API endpoints, not MVC controllers
builder.Services.AddOpenApi();

// Register MemoryCache for CapabilityStatement caching (Phase 1.2)
builder.Services.AddMemoryCache();

// Register RecyclableMemoryStreamManager as singleton
builder.Services.AddSingleton<RecyclableMemoryStreamManager>();

var terminologyAutoImportEnabled = builder.Configuration.GetValue<bool>("Terminology:EnableAutoImport", false);

// Register MultiTenantSearchIndexCache as singleton (multi-tenant cache consolidation)
// Provides per-tenant cache instances for search index reference data
// Uses on-demand caching for large datasets (Systems, QuantityCodes) to prevent memory exhaustion
builder.Services.AddSingleton<Ignixa.DataLayer.SqlEntityFramework.Indexing.MultiTenantSearchIndexCache>();

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

// Configure BackgroundService resilience - prevent one failing service from stopping the host
// This allows the FHIR server to remain available even if DurableTask or other background services fail
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// Configure blob storage options
builder.Services.Configure<Ignixa.DataLayer.BlobStorage.Infrastructure.LocalFileBlobStorageOptions>(
    builder.Configuration.GetSection("LocalFileBlobStorage"));

// Configure SearchParameter conflict resolution options (Phase 21+ - Multi-IG support)
// Maps from appsettings.json: SearchParameters:ConflictResolution
builder.Services.Configure<SearchParameterResolutionOptions>(
    builder.Configuration.GetSection("SearchParameters:ConflictResolution"));

// Register IHttpContextFactory and IHttpContextAccessor for bundle entry pipeline routing
builder.Services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();
builder.Services.AddHttpContextAccessor();

// Register IFhirRequestContextAccessor for centralized FHIR request context (Phase 1 - IFhirRequestContext pattern)
// Replaces scattered HttpContext.Items["TenantId"] extractions with strongly-typed context
// Uses AsyncLocal for thread-safe bundle processing with isolated contexts per entry
builder.Services.AddScoped<IFhirRequestContextAccessor, FhirRequestContextAccessor>();

// Register IndexLoaderService as hosted service
builder.Services.AddHostedService<IndexLoaderService>();

// Register TenantPackagePreloadService for package preloading at startup
// Loads packages configured in TenantConfiguration.Packages.PreloadPackages
// Embedded packages are loaded via EmbeddedPackageLoader when referenced in PreloadPackages
builder.Services.AddHostedService<TenantPackagePreloadService>();

// Register SqlReferenceDataPreloadHandler for SQL reference data cache warming
// Pre-warms ResourceType and SearchParam mappings to avoid DB hits on first bundle
// Triggered by TenantPackagePreloadCompletedEvent after packages are loaded
builder.Services.AddSingleton<SqlReferenceDataPreloadHandler>();

// Bootstrap service to trigger terminology imports for existing packages (runs after preload)
// DISABLED by default: previously caused startup performance issues. Enable via Terminology:EnableAutoImport=true.
if (terminologyAutoImportEnabled)
{
    builder.Services.AddHostedService<TerminologyImportBootstrapService>();
}

// Register HTTP client factory for background operations (Import activities need this)
builder.Services.AddHttpClient();

// Register DurableTask framework for background job processing ($export, $import)
builder.Services.AddDurableTask();

// Register MCP Server services (Phase 1 - ADR-2540)
// Provides AI-accessible tools for FHIR operations via Server-Sent Events (SSE) transport
// Tools are located in Ignixa.Application.Features.Mcp.Tools (Application layer)
// Can be disabled via configuration: Mcp:Enabled = false
var mcpEnabled = builder.Configuration.GetValue<bool>("Mcp:Enabled", true);
if (mcpEnabled)
{
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly(typeof(Ignixa.Application.Features.Mcp.Tools.DiagnosticTool).Assembly)
        .WithToolsFromAssembly(typeof(Ignixa.Application.BackgroundOperations.JobManagement.GetJobStatusTool).Assembly);
}

// Configure Autofac container
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    // Register InMemoryResourceLocationIndex
    containerBuilder.RegisterType<InMemoryResourceLocationIndex>()
        .As<IResourceLocationIndex>()
        .SingleInstance();

    // BACKGROUND OPERATIONS CONFIGURATION
    // Auto-register all DurableTask activities and orchestrations from the BackgroundOperations assembly
    // This scans for all classes inheriting from TaskActivity or TaskOrchestration and registers them with DI
    // New activities/orchestrations are automatically discovered without requiring manual registration updates
    var backgroundOpsAssembly = typeof(Ignixa.Application.BackgroundOperations.Export.Activities.ExportWorkerActivity).Assembly;

    // Register all TaskActivity types (Export/Import workers, job completion, etc.)
    containerBuilder.RegisterAssemblyTypes(backgroundOpsAssembly)
        .Where(t => typeof(DurableTask.Core.TaskActivity).IsAssignableFrom(t) && !t.IsAbstract)
        .AsSelf()
        .InstancePerDependency();

    // Register all TaskOrchestration types (ExportOrchestration, ImportOrchestration)
    // CRITICAL: Orchestrations MUST be registered or DurableTask cannot find them when executing jobs
    containerBuilder.RegisterAssemblyTypes(backgroundOpsAssembly)
        .Where(t => typeof(DurableTask.Core.TaskOrchestration).IsAssignableFrom(t) && !t.IsAbstract)
        .AsSelf()
        .InstancePerDependency();

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
    // Also register as itself for handlers that need direct access (e.g., PackageLoadedSearchParameterSyncHandler)
    containerBuilder.Register(c => new SqlEntityFrameworkRepositoryFactory(
            c.Resolve<ITenantConfigurationStore>(),
            c.Resolve<ILoggerFactory>(),
            c.Resolve<RecyclableMemoryStreamManager>(),
            c.Resolve<Ignixa.DataLayer.SqlEntityFramework.Indexing.MultiTenantSearchIndexCache>(),
            builder.Environment.EnvironmentName))
        .Named<IFhirRepositoryFactory>("SqlEf")
        .Named<ISearchServiceFactory>("SqlEf")
        .AsSelf()
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

    // Register IExportStreamWriterFactory for streaming export to blob storage
    // CompositeExportStreamWriterFactory determines format based on file extension:
    // - .ndjson → BlobStorageExportStreamWriter (NDJSON format with memory pooling)
    // - .parquet → ParquetExportStreamWriter (Parquet format with row buffering)
    // Used by ExportWorkerActivity to create writers that handle buffering and write directly to blob storage
    containerBuilder.RegisterType<Ignixa.DataLayer.BlobStorage.CompositeExportStreamWriterFactory>()
        .As<IExportStreamWriterFactory>()
        .SingleInstance();

    // Register ViewDefinitionLoader for SQL-on-FHIR ViewDefinition export
    // Used by ExportWorkerActivity to load ViewDefinition resources from the datastore
    // when _viewDefinition parameter is specified in export requests
    containerBuilder.RegisterType<Ignixa.DataLayer.BlobStorage.ViewDefinitionLoader>()
        .AsSelf()
        .SingleInstance();

    // Register open generic background job repository (in-memory for dev, SQL Server for production)
    // Supports both import and export with unified generic interface: IBackgroundJobRepository<T>
    // This single registration handles all job types: ImportJobDefinition, ExportJobDefinition, etc.
    containerBuilder.RegisterModule<BackgroundJobsModule>();


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

    // Validation operations ($validate - Phase 24: FHIR Operations)
    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.Validate.ValidateResourceHandler>()
        .As<IRequestHandler<Ignixa.Application.Operations.Features.Validate.ValidateResourceCommand, Ignixa.Application.Operations.Features.Validate.ValidateResourceResult>>()
        .InstancePerDependency();

    // Terminology operations ($expand, $translate, $subsumes)
    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.Terminology.Expand.ExpandValueSetHandler>()
        .As<IRequestHandler<Ignixa.Application.Operations.Features.Terminology.Expand.ExpandValueSetQuery, Ignixa.Application.Operations.Features.Terminology.Expand.ExpandValueSetResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.Terminology.Translate.TranslateCodeHandler>()
        .As<IRequestHandler<Ignixa.Application.Operations.Features.Terminology.Translate.TranslateCodeCommand, Ignixa.Application.Operations.Features.Terminology.Translate.TranslateCodeResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.Terminology.Subsumes.SubsumesHandler>()
        .As<IRequestHandler<Ignixa.Application.Operations.Features.Terminology.Subsumes.SubsumesQuery, Ignixa.Application.Operations.Features.Terminology.Subsumes.SubsumesQueryResult>>()
        .InstancePerDependency();

    // Patient $everything operation (Phase 25: FHIR $everything)
    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.PatientEverything.PatientEverythingHandler>()
        .As<IRequestHandler<Ignixa.Application.Operations.Features.PatientEverything.PatientEverythingQuery, SearchResourcesResult>>()
        .InstancePerDependency();

    // Transform operation ($transform - FHIR Mapping Language) - Phase 2
    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.Transform.TransformResourceHandler>()
        .As<IRequestHandler<Ignixa.Application.Operations.Features.Transform.TransformResourceCommand, ResourceJsonNode>>()
        .InstancePerLifetimeScope();

    // ConceptMap translation service for translate() FML function (Phase 2.2)
    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.Transform.ConceptMapResolverService>()
        .AsSelf()
        .InstancePerLifetimeScope();

    // FHIR Mapping Language (FML) dependencies - Phase 2
    containerBuilder.RegisterType<MappingParser>()
        .AsSelf()
        .SingleInstance();

    // StructureMapParser - scoped per-request, receives tenant FHIR version from request context
    containerBuilder.Register(c =>
        {
            var requestContext = c.Resolve<IFhirRequestContextAccessor>().RequestContext;
            var fhirVersion = requestContext?.FhirVersion;
            return new StructureMapParser(fhirVersion);
        })
        .AsSelf()
        .InstancePerLifetimeScope();

    // MapRegistryCache - scoped per-request to match StructureMapParser lifecycle (Phase 2.3)
    // Note: Cache is now per-request. If cross-request caching becomes necessary for performance,
    // consider implementing a factory pattern for StructureMapParser instead.
    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.Transform.MapRegistryCache>()
        .AsSelf()
        .As<IMapRegistry>()
        .InstancePerLifetimeScope();

    // Package loaded event handler for map cache invalidation (Phase 2.3)
    containerBuilder.RegisterType<Ignixa.Application.Operations.Events.PackageLoadedMapCacheInvalidationHandler>()
        .As<INotificationHandler<Ignixa.Application.Events.Package.PackageLoadedEvent>>()
        .InstancePerLifetimeScope();

    // FHIRPath expression caching (Phase 2.1)
    containerBuilder.RegisterType<Ignixa.Application.Operations.Features.Transform.FhirPathExpressionCache>()
        .AsSelf()
        .SingleInstance();

    // FHIRPath evaluator with timeout protection (Phase 2.1)
    containerBuilder.Register(c => new Ignixa.Application.Operations.Features.Transform.FhirPathEvaluatorWithTimeout(
            c.Resolve<Ignixa.Application.Operations.Features.Transform.FhirPathExpressionCache>(),
            c.Resolve<Ignixa.FhirPath.Evaluation.FhirPathEvaluator>(),
            TimeSpan.FromSeconds(5),
            c.Resolve<ILogger<Ignixa.Application.Operations.Features.Transform.FhirPathEvaluatorWithTimeout>>())) // 5-second timeout
        .AsSelf()
        .SingleInstance();

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

    containerBuilder.RegisterType<FhirPathParser>()
        .AsSelf()
        .InstancePerDependency();

    // IJsonNodeMutator for PATCH and Transform operations (shared mutation logic, Phase 2: request-aware via factory)
    // All PATCH executors use IJsonNodeMutator for consistent mutation logic
    // CRITICAL: Changed to InstancePerDependency to avoid capturing IComponentContext in closure
    // The schema provider factory needs access to IFhirRequestContextAccessor which is scoped per request
    containerBuilder.Register<Ignixa.FhirMappingLanguage.Mutator.IJsonNodeMutator>(c =>
    {
        var evaluator = c.Resolve<Ignixa.FhirPath.Evaluation.FhirPathEvaluator>();
        var parser = c.Resolve<FhirPathParser>();
        var versionContext = c.Resolve<IFhirVersionContext>();
        var requestContextAccessor = c.Resolve<IFhirRequestContextAccessor>();

        // Schema provider factory that uses ALREADY-RESOLVED dependencies
        // Since we're using InstancePerDependency, this mutator is created fresh for each injection
        // so requestContextAccessor points to the current request's context
        Func<ISchema> schemaProviderFactory = () =>
        {
            var requestContext = requestContextAccessor.RequestContext;

            if (requestContext is null)
            {
                // Fallback for tests or non-HTTP contexts
                return versionContext.GetBaseSchemaProvider(FhirVersion.R4);
            }

            return versionContext.GetSchemaProvider(
                requestContext.FhirVersion,
                requestContext.TenantId);
        };

        return new Ignixa.FhirMappingLanguage.Mutator.JsonNodeMutator(
            evaluator,
            parser,
            schemaProviderFactory);
    })
    .As<Ignixa.FhirMappingLanguage.Mutator.IJsonNodeMutator>()
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
    // Now supports tenant-aware composite providers for custom resource types
    // Requires SearchParameterResolutionOptions for intelligent conflict resolution
    containerBuilder.Register<IFhirVersionContext>(c =>
    {
        var configuration = c.Resolve<IConfiguration>();
        var options = new SearchParameterResolutionOptions();
        configuration.GetSection("SearchParameters:ConflictResolution").Bind(options);

        return new FhirVersionContext(
            c.Resolve<ILoggerFactory>(),
            options,
            c.Resolve<IPackageResourceRepository>(),
            c.Resolve<Ignixa.Abstractions.IPackageResourceProvider>(),
            c.Resolve<ICompositeSchemaProviderRegistry>());
    })
    .SingleInstance();

    // Register SearchOptionsBuilderFactory for version-aware search options builders
    // Factory creates and caches builders per (tenant, FHIR version) pair
    // Phase 1: Single-tenant mode (uses TenantContext.Default)
    // Phase 2+: Multi-tenant mode with custom search parameters per tenant
    // Now uses IFhirVersionContext to reuse cached managers
    containerBuilder.RegisterType<SearchOptionsBuilderFactory>()
        .As<ISearchOptionsBuilderFactory>()
        .SingleInstance();

    // Register ISearchOptionsBuilder as default (R4) for background operations
    // Background activities like ExportWorkerActivity need a direct ISearchOptionsBuilder
    // For Phase 1 (single-tenant), this is R4. Phase 2+ will need tenant-specific resolution
    containerBuilder.Register<ISearchOptionsBuilder>(c =>
    {
        var factory = c.Resolve<ISearchOptionsBuilderFactory>();
        return factory.Create(FhirVersion.R4);
    }).SingleInstance();

    // Register FhirSchemaProviderResolver - enables version-aware components to resolve
    // the correct provider at runtime based on request FHIR version
    // Note: GetSchemaProvider now requires tenantId parameter, so consumers should call it directly
    // This registration is kept for backward compatibility but defaults to tenant 1
    containerBuilder.Register<Func<FhirVersion, IFhirSchemaProvider>>(c =>
    {
        var versionContext = c.Resolve<IFhirVersionContext>();
        return (FhirVersion version) => versionContext.GetSchemaProvider(version, tenantId: null);
    }).SingleInstance();

    // DEPRECATED: VersionAwareSearchParameterDefinitionManager replaced by FhirVersionContext.GetSearchParameterDefinitionManager()
    // Multi-version support now provided via FhirVersionContext pattern (same as SearchIndexer and SchemaProvider)
    // For single-tenant mode (Phase 1), default to R4 for backward compatibility
    containerBuilder.Register<ISearchParameterDefinitionManager>(c =>
    {
        var versionContext = c.Resolve<IFhirVersionContext>();
        // Single-tenant mode: default to R4 for backward compatibility
        // Multi-tenant mode: will be resolved from tenant-specific FHIR version in request context
        return versionContext.GetSearchParameterDefinitionManager(FhirVersion.R4);
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
        return versionContext.GetCompartmentDefinitionManager(FhirVersion.R4);
    }).SingleInstance();

    // NOTE: ReferenceSearchValueParser, SearchParameterExpressionParser, ExpressionParser, and SearchOptionsBuilder
    // are now created by SearchOptionsBuilderFactory with version-specific dependencies
    // No longer registered in DI container - factory creates them per (tenant, version) pair

    // PHASE 1.2: Segmented CapabilityStatement with Smart Caching

    // Register IApplicationVersionInfo (provides assembly version for CapabilityStatement)
    // Retrieves version from GitVersion assembly attributes, falls back to "0.0.0-dev" if unavailable
    containerBuilder.RegisterType<Ignixa.Application.Infrastructure.ApplicationVersionInfo>()
        .As<IApplicationVersionInfo>()
        .SingleInstance();

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

    containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.Segments.OperationsSegment>()
        .As<Ignixa.Application.Features.Metadata.Segments.ICapabilitySegment>()
        .InstancePerLifetimeScope();


    // NOTE: CustomResourceTypeCapabilitySegment removed - custom resource types now included
    // via FhirVersionContext.GetSchemaProvider(version, tenantId) in ResourceInteractionCapabilitySegment

    // Background operations handlers (bulk export, bulk import)
    containerBuilder.RegisterType<Ignixa.Application.BackgroundOperations.Export.CreateExportJobHandler>()
        .As<IRequestHandler<Ignixa.Application.BackgroundOperations.Export.CreateExportJobCommand, Ignixa.Application.BackgroundOperations.Export.CreateExportJobResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.BackgroundOperations.Jobs.GetJobStatusHandler>()
        .As<IRequestHandler<Ignixa.Application.BackgroundOperations.Jobs.GetJobStatusQuery, Ignixa.Application.BackgroundOperations.Jobs.GetJobStatusResult>>()
        .InstancePerDependency();

    // PACKAGE FEATURES REGISTRATION
    // Register package features that declare implementation of loaded FHIR packages
    // Each feature declares what operations it implements from a package
    // OperationsSegment uses these to conditionally expose operations in CapabilityStatement
    containerBuilder.RegisterType<Ignixa.Application.Features.Export.BulkDataExportFeature>()
        .As<Ignixa.Domain.Abstractions.IPackageFeature>()
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
    containerBuilder.RegisterType<FhirPathParser>()
        .AsSelf()
        .SingleInstance();

    // Register validation schema builder (shared, creates schemas from StructureDefinitions)
    containerBuilder.Register(c =>
        {
            var compiler = c.Resolve<FhirPathParser>();
            return new Ignixa.Validation.Schema.StructureDefinitionSchemaBuilder(compiler);
        })
        .AsSelf()
        .SingleInstance();

    // Register PackageResourceProvider (converts package JSON to IType)
    containerBuilder.RegisterType<PackageResourceProvider>()
        .As<Ignixa.Abstractions.IPackageResourceProvider>()
        .SingleInstance();

    // Register ValidationSchemaResolver FACTORY (creates version+tenant-specific cached resolvers)
    // New signature: Func<FhirVersion, int, IValidationSchemaResolver>
    // Usage: var resolver = factory(FhirVersion.R4, tenantId)
    // Uses FhirVersionContext.GetSchemaProvider(version, tenantId) for composite provider
    containerBuilder.Register<Func<FhirVersion, int, Ignixa.Validation.Abstractions.IValidationSchemaResolver>>(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            var builder = c.Resolve<Ignixa.Validation.Schema.StructureDefinitionSchemaBuilder>();
            var terminologyService = c.Resolve<Ignixa.Validation.Abstractions.ITerminologyService>();

            return (version, tenantId) =>
            {
                // Get tenant-aware schema provider (includes custom resource types from loaded packages)
                var schemaProvider = versionContext.GetSchemaProvider(version, tenantId);

                // Pass ISchema and terminology service to StructureDefinitionSchemaResolver for binding validation
                var resolver = new Ignixa.Validation.Schema.StructureDefinitionSchemaResolver(schemaProvider, builder, terminologyService);
                return new Ignixa.Validation.Schema.CachedValidationSchemaResolver(resolver);
            };
        })
        .SingleInstance();

    // Register backward-compatible single-tenant factory (defaults to tenant 1)
    containerBuilder.Register<Func<FhirVersion, Ignixa.Validation.Abstractions.IValidationSchemaResolver>>(c =>
        {
            var multiTenantFactory = c.Resolve<Func<FhirVersion, int, Ignixa.Validation.Abstractions.IValidationSchemaResolver>>();
            return (version) => multiTenantFactory(version, 1); // Default to tenant 1
        })
        .SingleInstance();

    // TERMINOLOGY SERVICES - HYBRID ROUTING (Phase 2 Week 3 - ADR-2533)
    // HybridTerminologyService routes to SQL (fast) when terminology is imported,
    // falls back to InMemoryTerminologyService (JSON) when not imported

    // Register InMemoryTerminologyService as fallback for non-imported terminology
    // Resolves FhirVersion from the current request context (tenant resolution)
    containerBuilder.Register<Ignixa.Validation.Services.InMemoryTerminologyService>(c =>
        {
            var requestContext = c.Resolve<IFhirRequestContextAccessor>().RequestContext;
            var fhirVersion = requestContext?.FhirVersion ?? FhirVersion.R4; // Default to R4 if no context
            return new Ignixa.Validation.Services.InMemoryTerminologyService(fhirVersion);
        })
        .AsSelf()
        .InstancePerLifetimeScope();

    // Register SqlTerminologyService as concrete type (dependency for HybridTerminologyService)
    containerBuilder.RegisterType<SqlTerminologyService>()
        .AsSelf()
        .InstancePerLifetimeScope();


    // Register HybridTerminologyService as ITerminologyService
    // Routes to SQL (fast) when terminology is imported, fallback to JSON parsing otherwise
    containerBuilder.Register<Ignixa.Validation.Abstractions.ITerminologyService>(c =>
    {
        var sqlService = c.Resolve<SqlTerminologyService>();
        var fallbackService = c.Resolve<Ignixa.Validation.Services.InMemoryTerminologyService>();
        var logger = c.Resolve<ILogger<HybridTerminologyService>>();

        return new HybridTerminologyService(
            sqlService,
            fallbackService,
            logger);
    })
    .InstancePerLifetimeScope();
 // Share within request scope for caching efficiency

    // PACKAGE MANAGEMENT (ADR-2532 Phase 1: Package Integration)
    // Provides NPM package download, extraction, and conformance resource caching

    // Register HttpClient for NpmPackageLoader with resilience policies (downloads from packages.fhir.org)
    builder.Services.AddTransient<Ignixa.PackageManagement.Infrastructure.ResilientHttpMessageHandler>();
    builder.Services.AddHttpClient<Ignixa.PackageManagement.Infrastructure.NpmPackageLoader>()
        .ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
            sp.GetRequiredService<Ignixa.PackageManagement.Infrastructure.ResilientHttpMessageHandler>());

    // Register HttpClient for NpmPackageSearchService (searches packages.fhir.org catalog)
    builder.Services.AddHttpClient<Ignixa.PackageManagement.Infrastructure.NpmPackageSearchService>()
        .ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
            sp.GetRequiredService<Ignixa.PackageManagement.Infrastructure.ResilientHttpMessageHandler>());

    // Register embedded packages (discoverable via IEmbeddedPackage)
    containerBuilder.RegisterType<SqlOnFhirEmbeddedPackage>()
        .As<Ignixa.Abstractions.IEmbeddedPackage>()
        .SingleInstance();

    // Register composite package loader: embedded -> npm
    // Embedded packages (like SQL-on-FHIR ViewDefinition) load from assembly
    // Official packages fallback to NPM registry (packages.fhir.org)
    containerBuilder.Register<Ignixa.PackageManagement.Abstractions.IPackageLoader>(c =>
    {
        var loggerFactory = c.Resolve<ILoggerFactory>();
        var httpClient = c.Resolve<HttpClient>();
        var configuration = c.Resolve<IConfiguration>();

        // Create single embedded loader with all registered packages
        var embeddedPackages = c.Resolve<IEnumerable<Ignixa.Abstractions.IEmbeddedPackage>>();
        var embeddedLoader = new Ignixa.PackageManagement.Infrastructure.EmbeddedPackageLoader(
            embeddedPackages,
            loggerFactory.CreateLogger<Ignixa.PackageManagement.Infrastructure.EmbeddedPackageLoader>());

        // Configure NPM package loader options (allows overriding registry URL for testing)
        // Can be configured via appsettings.json: PackageManagement:NpmRegistry:RegistryUrl
        var npmOptions = new NpmPackageLoaderOptions
        {
            RegistryUrl = configuration.GetValue<string>(
                "PackageManagement:NpmRegistry:RegistryUrl",
                "https://packages.fhir.org"),
            EnableRetryPolicies = configuration.GetValue<bool>(
                "PackageManagement:NpmRegistry:EnableRetryPolicies",
                true)
        };

        var npmLoader = new Ignixa.PackageManagement.Infrastructure.NpmPackageLoader(
            httpClient,
            cacheManager: null,  // Caching handled by PackageExtractor
            options: npmOptions,
            loggerFactory.CreateLogger<Ignixa.PackageManagement.Infrastructure.NpmPackageLoader>());

        // Create composite loader with embedded loader first, then npm
        return new Ignixa.PackageManagement.Infrastructure.CompositePackageLoader(
            loggerFactory.CreateLogger<Ignixa.PackageManagement.Infrastructure.CompositePackageLoader>(),
            embeddedLoader,
            npmLoader);
    })
    .As<Ignixa.PackageManagement.Abstractions.IPackageLoader>()
    .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.PackageManagement.Infrastructure.PackageExtractor>()
        .As<Ignixa.PackageManagement.Abstractions.IPackageExtractor>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.PackageManagement.Infrastructure.PackageResourceImporter>()
        .As<Ignixa.PackageManagement.Abstractions.IPackageResourceImporter>()
        .InstancePerDependency();

    // Register global IPackageResourceRepository (shared across all tenants for conformance resources)
    // NOTE: Package resources are tenant-agnostic. Phase 2: will be tenant-scoped.
    // CRITICAL: Registered as InstancePerDependency (NOT SingleInstance) because DbContext is not thread-safe.
    // Each concurrent request MUST get its own DbContext instance.
    // Sharing a single DbContext across threads causes: "A second operation was started on this context
    // instance before a previous operation completed"
    // See: https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/
    // Register PackageRepositoryDbContextFactory as SingleInstance (thread-safe factory)
    containerBuilder.Register<Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement.PackageRepositoryDbContextFactory>(c =>
    {
        var tenantStore = c.Resolve<ITenantConfigurationStore>();
        var loggerFactory = c.Resolve<ILoggerFactory>();

        // Get Tenant 1 connection string (system partition inherits from this)
        // CA2012: Convert ValueTask to Task before blocking
        var tenantConfig = tenantStore.GetTenantConfigurationAsync(1, default).AsTask().GetAwaiter().GetResult();
        if (tenantConfig == null || string.IsNullOrEmpty(tenantConfig.Storage.ConnectionString))
        {
            throw new InvalidOperationException("Tenant 1 connection string is required for global package resource repository");
        }

        return new Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement.PackageRepositoryDbContextFactory(
            tenantConfig.Storage.ConnectionString,
            loggerFactory);
    })
    .SingleInstance();

    // Register SqlPackageResourceRepository (creates fresh DbContext per operation via factory)
    containerBuilder.Register<IPackageResourceRepository>(c =>
        new Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement.SqlPackageResourceRepository(
            c.Resolve<Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement.PackageRepositoryDbContextFactory>(),
            c.Resolve<ILogger<Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement.SqlPackageResourceRepository>>()))
    .InstancePerDependency();

    containerBuilder.Register<Ignixa.PackageManagement.Abstractions.IImplementationGuideProvider>(c =>
        new Ignixa.PackageManagement.Infrastructure.ImplementationGuideProvider(
            c.Resolve<Ignixa.PackageManagement.Abstractions.IPackageLoader>(),
            c.Resolve<Ignixa.PackageManagement.Abstractions.IPackageExtractor>(),
            c.Resolve<Ignixa.PackageManagement.Abstractions.IPackageResourceImporter>(),
            c.Resolve<IPackageResourceRepository>(),
            c.Resolve<ILogger<Ignixa.PackageManagement.Infrastructure.ImplementationGuideProvider>>()))
        .SingleInstance();

    // Register NPM package search service for MCP tools (package discovery and name resolution)
    containerBuilder.Register<Ignixa.PackageManagement.Abstractions.INpmPackageSearchService>(c =>
    {
        var configuration = c.Resolve<IConfiguration>();
        var httpClient = c.Resolve<HttpClient>();
        var loggerFactory = c.Resolve<ILoggerFactory>();

        // Configure NPM package search options (same registry URL as package loader)
        var npmOptions = new NpmPackageLoaderOptions
        {
            RegistryUrl = configuration.GetValue<string>(
                "PackageManagement:NpmRegistry:RegistryUrl",
                "https://packages.fhir.org"),
            EnableRetryPolicies = configuration.GetValue<bool>(
                "PackageManagement:NpmRegistry:EnableRetryPolicies",
                true)
        };

        return new Ignixa.PackageManagement.Infrastructure.NpmPackageSearchService(
            httpClient,
            npmOptions,
            loggerFactory.CreateLogger<Ignixa.PackageManagement.Infrastructure.NpmPackageSearchService>());
    })
    .As<Ignixa.PackageManagement.Abstractions.INpmPackageSearchService>()
    .SingleInstance();

    // Register conformance resource caching
    containerBuilder.RegisterType<Ignixa.Domain.Caching.InMemoryConformanceCache>()
        .As<Ignixa.Domain.Caching.IFhirConformanceCache>()
        .SingleInstance();

    // Register conformance resource resolver (fallback chain: Cache → DB → null)
    // NOTE: Currently uses global IPackageResourceRepository. Phase 2: will be tenant-scoped.
    containerBuilder.RegisterType<Ignixa.Domain.Caching.ConformanceResourceResolver>()
        .As<Ignixa.Domain.Abstractions.IConformanceResourceResolver>()
        .SingleInstance();

    // Register package management command/query handlers
    containerBuilder.RegisterType<Ignixa.Application.Features.Admin.LoadPackageHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.Admin.LoadPackageCommand, Ignixa.Application.Features.Admin.LoadPackageResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.Admin.ListPackagesHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.Admin.ListPackagesQuery, Ignixa.Application.Features.Admin.ListPackagesResult>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<Ignixa.Application.Features.Admin.UnloadPackageHandler>()
        .As<IRequestHandler<Ignixa.Application.Features.Admin.UnloadPackageCommand, Ignixa.Application.Features.Admin.UnloadPackageResult>>()
        .InstancePerDependency();

    // Register composite schema provider registry for cache invalidation
    // Uses 1 second debounce delay to batch invalidations during bulk package loads
    containerBuilder.Register<ICompositeSchemaProviderRegistry>(c =>
        new CompositeSchemaProviderRegistry(
            c.Resolve<ILogger<CompositeSchemaProviderRegistry>>(),
            debounceDelay: TimeSpan.FromSeconds(1)))
        .SingleInstance();

    // Register PackageLoaded event handlers
    containerBuilder.RegisterType<Ignixa.Application.Events.Package.PackageLoadedNotificationHandler>()
        .As<INotificationHandler<Ignixa.Application.Events.Package.IPackageLoaded>>()
        .InstancePerDependency();

    // Register PackageLoadedSearchParameterSyncHandler for syncing search parameters to database
    // CRITICAL: Without this, US Core and other package search parameters won't be in the database
    // and bundle processing will fail with "SearchParam URL not found" warnings
    containerBuilder.RegisterType<Ignixa.DataLayer.SqlEntityFramework.Events.PackageLoadedSearchParameterSyncHandler>()
        .As<INotificationHandler<Ignixa.Application.Events.Package.PackageLoadedEvent>>()
        .InstancePerDependency();

    // Register PackageLoadedTerminologyImportHandler for automatic terminology import after package load
    // Guarded by Terminology:EnableAutoImport to avoid startup perf issues when large packages are present.
    if (terminologyAutoImportEnabled)
    {
        containerBuilder.RegisterType<Ignixa.DataLayer.SqlEntityFramework.Events.PackageLoadedTerminologyImportHandler>()
            .As<INotificationHandler<Ignixa.Application.Events.Package.PackageLoadedEvent>>()
            .InstancePerDependency();
    }

    // Register TerminologyImportTriggeredHandler to start DurableTask orchestration
    containerBuilder.RegisterType<Ignixa.Application.BackgroundOperations.Terminology.EventHandlers.TerminologyImportTriggeredHandler>()
        .As<INotificationHandler<Ignixa.Application.Events.Terminology.TerminologyImportTriggeredEvent>>()
        .InstancePerDependency();

    // TERMINOLOGY SERVICES (Phase 2 Week 3 - ADR-2533: FHIR Terminology Services)
    // Provides CodeSystem/ValueSet/ConceptMap import from packages into SQL tables

    // Register SqlSystemRepository for system URL normalization
    containerBuilder.RegisterType<SqlSystemRepository>()
        .As<ISystemRepository>()
        .InstancePerDependency();

    // Terminology importer is constructed inside ImportTerminologyResourceActivity using tenant-scoped DbContext
    // to avoid leaking DbContext through singleton registrations. No direct ITerminologyImporter registration here.

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

// FHIR REQUEST CONTEXT MIDDLEWARE (Phase 1 - IFhirRequestContext pattern)
// Creates centralized IFhirRequestContext with tenant, FHIR version, resource type, and bundle state
// Runs AFTER TenantResolutionMiddleware to access tenant information from HttpContext.Items
// Provides strongly-typed context accessible via IFhirRequestContextAccessor throughout request pipeline
app.UseMiddleware<FhirRequestContextMiddleware>();

// DEVELOPMENT VALIDATION: Detect middleware ordering issues
// Ensures TenantResolutionMiddleware runs before FhirRequestContextMiddleware
// Catches configuration errors early in dev where TenantId would be 0/null
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var fhirContextAccessor = context.RequestServices.GetRequiredService<IFhirRequestContextAccessor>();

        if (context.GetEndpoint() != null &&
            !context.Items.ContainsKey("TenantId") &&
            fhirContextAccessor.RequestContext?.TenantId == 0)
        {
            logger.LogWarning(
                "TenantResolutionMiddleware may not have run before FhirRequestContextMiddleware. " +
                "Route: {Path}, TenantId in context: {TenantId}",
                context.Request.Path,
                fhirContextAccessor.RequestContext?.TenantId);
        }

        await next();
    });
}

// CAPABILITY ENFORCEMENT (Phase 3 - ADR-2506)
// Now handled by CapabilityEnforcementBehavior (Medino pipeline behavior)
// Commands/queries implement IRequiresCapability and declare FHIRPath expressions
// Behavior validates requests against CapabilityStatement before executing handlers

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// HTTPS redirection - disabled for MCP development (self-signed cert issues with VS Code)
// Enable in production with valid certificates
// app.UseHttpsRedirection();

// Map health check endpoints (before FHIR endpoints, bypasses tenant resolution)
app.MapHealthCheckEndpoints();

// IMPORTANT: Register bulk operations BEFORE generic FHIR endpoints
// This ensures /$import and /$export routes match before the generic /{resourceType} catch-all
app.MapExportEndpoints(); // Bulk export endpoints (DurableTask)
app.MapImportEndpoints(); // Bulk import endpoints (DurableTask)
app.MapAdminPackageEndpoints(); // Admin package management endpoints (/admin/packages)

app.MapFhirEndpoints();
app.MapFhirHistoryEndpoints(); // FHIR _history endpoints (instance, type, system-level)
app.MapOperationEndpoints(); // FHIR operation endpoints ($validate, etc.)
app.MapTerminologyEndpoints(); // FHIR terminology endpoints ($expand, $translate, $subsumes)
app.MapPatchEndpoints(); // FHIR PATCH endpoints (direct and conditional)
app.MapCompartmentEndpoints(); // FHIR compartment search endpoints (GET /Patient/123/Observation)
app.MapMetadataEndpoints(); // FHIR metadata endpoints (CapabilityStatement)

// MCP (Model Context Protocol) Integration (Phase 1 - ADR-2540)
// Provides AI-accessible tools for FHIR operations via Server-Sent Events (SSE) transport
// Only map endpoints if MCP is enabled in configuration
if (mcpEnabled)
{
    app.MapMcpEndpoints();
}

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
    var startupTiming = app.Services.GetRequiredService<StartupTimingDiagnostics>();

    logger.LogInformation("===== Database Initialization =====");

    // Include system partition (tenant 0) which GetAllTenantsAsync excludes
    // This ensures system partition DB is initialized upfront rather than lazily during package loading
    var systemPartition = await configStore.GetTenantConfigurationAsync(SystemConstants.SystemPartitionId);
    var tenants = await configStore.GetAllTenantsAsync();
    var allTenantsToInit = new List<TenantConfiguration>();
    if (systemPartition?.IsActive == true)
    {
        allTenantsToInit.Add(systemPartition);
    }
    allTenantsToInit.AddRange(tenants);

    foreach (var tenant in allTenantsToInit)
    {
        try
        {
            using (startupTiming.StartPhase($"Database.Init.Tenant{tenant.TenantId}"))
            {
                logger.LogInformation("Initializing database for tenant {TenantId} ({DisplayName})...", tenant.TenantId, tenant.DisplayName);

                // This will trigger SqlEntityFrameworkRepositoryFactory to create the repository
                // which internally calls DatabaseInitializer.InitializeAsync() to apply migrations
                var repository = await repositoryFactory.GetRepositoryAsync(tenant.TenantId);

                logger.LogInformation("✅ Database initialized for tenant {TenantId}", tenant.TenantId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to initialize database for tenant {TenantId} ({DisplayName}). Error: {Message}",
                tenant.TenantId, tenant.DisplayName, ex.Message);
            throw;
        }
    }

    logger.LogInformation("===== All Databases Initialized =====");

    // Log startup timing summary
    startupTiming.LogSummary();
}

await app.RunAsync();

// Explicit partial class to make Program public for integration testing
public partial class Program { }
