// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Api.Infrastructure;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.Compartment;
using Ignixa.Application.Features.ConditionalOperations.ConditionalCreate;
using Ignixa.Application.Features.ConditionalOperations.ConditionalDelete;
using Ignixa.Application.Features.ConditionalOperations.ConditionalPatch;
using Ignixa.Application.Features.ConditionalOperations.ConditionalRead;
using Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate;
using Ignixa.Application.Features.Export;
using Ignixa.Application.Features.History;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.Application.Features.Patch.Validation;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Behaviors;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Application.Operations.Features.PatientEverything;
using Ignixa.Application.Operations.Features.Terminology.Expand;
using Ignixa.Application.Operations.Features.Terminology.Subsumes;
using Ignixa.Application.Operations.Features.Terminology.Translate;
using Ignixa.Application.Operations.Features.Transform;
using Ignixa.Application.Operations.Features.Validate;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers application layer services including Medino handlers, pipeline behaviors,
/// and business logic components.
/// </summary>
public static class ApplicationServicesRegistration
{
    /// <summary>
    /// Registers application services in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterApplicationServices(
        this ContainerBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Medino (MediatR-like) infrastructure
        RegisterMedinoServices(builder);

        // Resource CRUD handlers
        RegisterResourceHandlers(builder);

        // Conditional operations handlers
        RegisterConditionalOperationHandlers(builder);

        // History handlers
        RegisterHistoryHandlers(builder);

        // FHIR operations handlers ($validate, $expand, etc.)
        RegisterOperationHandlers(builder);

        // Patch handlers
        RegisterPatchServices(builder);

        // FHIR Mapping Language (FML) services
        RegisterFmlServices(builder);

        // CapabilityStatement services
        RegisterCapabilityServices(builder);

        // Bundle processing services
        RegisterBundleServices(builder);

        // Package management handlers
        RegisterPackageManagementHandlers(builder);

        // Event handlers
        RegisterEventHandlers(builder, configuration);

        // Authorization services
        RegisterAuthorizationServices(builder);

        return builder;
    }

    private static void RegisterMedinoServices(ContainerBuilder builder)
    {
        // Medino service provider
        builder.Register<IMediatorServiceProvider>(c =>
        {
            var context = c.Resolve<IComponentContext>();
            return new AutofacMediatorServiceProvider(context);
        }).SingleInstance();

        // Medino mediator
        builder.RegisterType<Mediator>().As<IMediator>().SingleInstance();

        // Pipeline behaviors
        builder.RegisterGeneric(typeof(CapabilityEnforcementBehavior<,>))
            .As(typeof(IPipelineBehavior<,>))
            .InstancePerLifetimeScope();

        builder.RegisterType<ValidationBehavior>()
            .As<IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>>()
            .InstancePerLifetimeScope();
    }

    private static void RegisterResourceHandlers(ContainerBuilder builder)
    {
        builder.RegisterType<GetResourceHandler>()
            .As<IRequestHandler<GetResourceQuery, SearchEntryResult?>>()
            .InstancePerDependency();

        builder.RegisterType<CreateOrUpdateResourceHandler>()
            .As<IRequestHandler<CreateOrUpdateResourceCommand, UpdateResult>>()
            .InstancePerDependency();

        builder.RegisterType<DeleteResourceHandler>()
            .As<IRequestHandler<DeleteResourceCommand, bool>>()
            .InstancePerDependency();

        builder.RegisterType<SearchResourcesHandler>()
            .As<IRequestHandler<SearchResourcesQuery, SearchResourcesResult>>()
            .InstancePerDependency();

        builder.RegisterType<SearchCompartmentHandler>()
            .As<IRequestHandler<SearchCompartmentQuery, SearchResourcesResult>>()
            .InstancePerDependency();
    }

    private static void RegisterConditionalOperationHandlers(ContainerBuilder builder)
    {
        builder.RegisterType<ConditionalCreateHandler>()
            .As<IRequestHandler<ConditionalCreateCommand, ConditionalCreateResult>>()
            .InstancePerDependency();

        builder.RegisterType<ConditionalUpdateHandler>()
            .As<IRequestHandler<ConditionalUpdateCommand, ConditionalUpdateResult>>()
            .InstancePerDependency();

        builder.RegisterType<ConditionalDeleteHandler>()
            .As<IRequestHandler<ConditionalDeleteCommand, ConditionalDeleteResult>>()
            .InstancePerDependency();

        builder.RegisterType<ConditionalPatchHandler>()
            .As<IRequestHandler<ConditionalPatchCommand, ConditionalPatchResult>>()
            .InstancePerDependency();

        builder.RegisterType<ConditionalReadHandler>()
            .As<IRequestHandler<ConditionalReadQuery, ConditionalReadResult>>()
            .InstancePerDependency();
    }

    private static void RegisterHistoryHandlers(ContainerBuilder builder)
    {
        builder.RegisterType<GetResourceHistoryHandler>()
            .As<IRequestHandler<GetResourceHistoryQuery, HistoryResult>>()
            .InstancePerDependency();

        builder.RegisterType<GetTypeHistoryHandler>()
            .As<IRequestHandler<GetTypeHistoryQuery, HistoryResult>>()
            .InstancePerDependency();

        builder.RegisterType<GetSystemHistoryHandler>()
            .As<IRequestHandler<GetSystemHistoryQuery, HistoryResult>>()
            .InstancePerDependency();
    }

    private static void RegisterOperationHandlers(ContainerBuilder builder)
    {
        // $validate
        builder.RegisterType<ValidateResourceHandler>()
            .As<IRequestHandler<ValidateResourceCommand, ValidateResourceResult>>()
            .InstancePerDependency();

        // Terminology operations
        builder.RegisterType<ExpandValueSetHandler>()
            .As<IRequestHandler<ExpandValueSetQuery, ExpandValueSetResult>>()
            .InstancePerDependency();

        builder.RegisterType<TranslateCodeHandler>()
            .As<IRequestHandler<TranslateCodeCommand, TranslateCodeResult>>()
            .InstancePerDependency();

        builder.RegisterType<SubsumesHandler>()
            .As<IRequestHandler<SubsumesQuery, SubsumesQueryResult>>()
            .InstancePerDependency();

        // Patient $everything
        builder.RegisterType<PatientEverythingHandler>()
            .As<IRequestHandler<PatientEverythingQuery, SearchResourcesResult>>()
            .InstancePerDependency();

        // $transform (FHIR Mapping Language)
        builder.RegisterType<TransformResourceHandler>()
            .As<IRequestHandler<TransformResourceCommand, ResourceJsonNode>>()
            .InstancePerLifetimeScope();

        // ConceptMap translation service
        builder.RegisterType<ConceptMapResolverService>()
            .AsSelf()
            .InstancePerLifetimeScope();
    }

    private static void RegisterPatchServices(ContainerBuilder builder)
    {
        // Patch handler
        builder.RegisterType<PatchResourceHandler>()
            .As<IRequestHandler<PatchResourceCommand, ResourceWrapper?>>()
            .InstancePerDependency();

        // Patch parsing and engine
        builder.RegisterType<FhirPatchParametersParser>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<FhirPatchEngine>()
            .AsSelf()
            .InstancePerDependency();

        // Patch validators
        builder.RegisterType<FhirPatchValidator>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<ImmutablePropertyValidator>()
            .AsSelf()
            .InstancePerDependency();

        // FHIRPath dependencies
        builder.RegisterType<FhirPathEvaluator>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<FhirPathParser>()
            .AsSelf()
            .InstancePerDependency();

        // JsonNodeMutator for PATCH and Transform operations
        builder.Register<IJsonNodeMutator>(c =>
        {
            var evaluator = c.Resolve<FhirPathEvaluator>();
            var parser = c.Resolve<FhirPathParser>();
            var versionContext = c.Resolve<IFhirVersionContext>();
            var requestContextAccessor = c.Resolve<IFhirRequestContextAccessor>();

            Func<ISchema> schemaProviderFactory = () =>
            {
                var requestContext = requestContextAccessor.RequestContext;
                if (requestContext is null)
                {
                    return versionContext.GetBaseSchemaProvider(FhirVersion.R4);
                }
                return versionContext.GetSchemaProvider(requestContext.FhirVersion, requestContext.TenantId);
            };

            return new JsonNodeMutator(evaluator, parser, schemaProviderFactory);
        })
        .As<IJsonNodeMutator>()
        .InstancePerDependency();

        // Patch operation executors
        builder.RegisterType<AddOperationExecutor>()
            .As<IOperationExecutor>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<InsertOperationExecutor>()
            .As<IOperationExecutor>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<DeleteOperationExecutor>()
            .As<IOperationExecutor>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<ReplaceOperationExecutor>()
            .As<IOperationExecutor>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<MoveOperationExecutor>()
            .As<IOperationExecutor>()
            .AsSelf()
            .InstancePerLifetimeScope();
    }

    private static void RegisterFmlServices(ContainerBuilder builder)
    {
        // FHIR Mapping Language parser
        builder.RegisterType<MappingParser>()
            .AsSelf()
            .SingleInstance();

        // StructureMapParser (scoped per-request, receives tenant FHIR version)
        builder.Register(c =>
        {
            var requestContext = c.Resolve<IFhirRequestContextAccessor>().RequestContext;
            var fhirVersion = requestContext?.FhirVersion;
            return new StructureMapParser(fhirVersion);
        })
        .AsSelf()
        .InstancePerLifetimeScope();

        // MapRegistryCache (scoped per-request)
        builder.RegisterType<Ignixa.Application.Operations.Features.Transform.MapRegistryCache>()
            .AsSelf()
            .As<IMapRegistry>()
            .InstancePerLifetimeScope();

        // FHIRPath expression caching
        builder.RegisterType<FhirPathExpressionCache>()
            .AsSelf()
            .SingleInstance();

        // FHIRPath evaluator with timeout protection
        builder.Register(c => new FhirPathEvaluatorWithTimeout(
                c.Resolve<FhirPathExpressionCache>(),
                c.Resolve<FhirPathEvaluator>(),
                TimeSpan.FromSeconds(5),
                c.Resolve<ILogger<FhirPathEvaluatorWithTimeout>>()))
            .AsSelf()
            .SingleInstance();
    }

    private static void RegisterCapabilityServices(ContainerBuilder builder)
    {
        // Capability cache
        builder.RegisterType<MemoryCapabilityCache>()
            .As<ICapabilityCache>()
            .SingleInstance();

        // Capability segments
        builder.RegisterType<StaticCapabilitySegment>()
            .As<ICapabilitySegment>()
            .SingleInstance();

        builder.RegisterType<ResourceInteractionCapabilitySegment>()
            .As<ICapabilitySegment>()
            .SingleInstance();

        builder.RegisterType<SearchParameterCapabilitySegment>()
            .As<ICapabilitySegment>()
            .SingleInstance();

        builder.RegisterType<IncludeRevIncludeCapabilitySegment>()
            .As<ICapabilitySegment>()
            .SingleInstance();

        builder.RegisterType<ProfileCapabilitySegment>()
            .As<ICapabilitySegment>()
            .SingleInstance();

        builder.RegisterType<OperationsSegment>()
            .As<ICapabilitySegment>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SecurityCapabilitySegment>()
            .As<ICapabilitySegment>()
            .SingleInstance();

        // CapabilityStatement service and handler
        builder.RegisterType<CapabilityStatementService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GetCapabilityStatementHandler>()
            .As<IRequestHandler<GetCapabilityStatementQuery, Ignixa.Application.Features.Metadata.Models.CapabilityStatementJsonNode>>()
            .InstancePerDependency();

        // Cache invalidator
        builder.RegisterType<CapabilityCacheInvalidator>()
            .As<ICapabilityCacheInvalidator>()
            .SingleInstance();
    }

    private static void RegisterBundleServices(ContainerBuilder builder)
    {
        builder.RegisterType<BundleReferencePreProcessor>()
            .InstancePerDependency();

        builder.RegisterType<BundleEntryExecutor>()
            .InstancePerDependency();

        builder.RegisterType<BundleChannelExecutor>()
            .InstancePerDependency();

        builder.RegisterType<BundleResponseBuilder>()
            .InstancePerDependency();

        builder.RegisterType<BundleProcessor>()
            .InstancePerDependency();

        builder.RegisterType<StreamingBundleParser>()
            .InstancePerDependency();

        // Pipeline executor for bundle entry routing
        builder.Register(c =>
        {
            var endpointDataSource = c.Resolve<EndpointDataSource>();
            var matcherPolicies = c.Resolve<IEnumerable<Microsoft.AspNetCore.Routing.MatcherPolicy>>();
            var endpointSelector = c.Resolve<Microsoft.AspNetCore.Routing.Matching.EndpointSelector>();
            var templateBinderFactory = c.Resolve<Microsoft.AspNetCore.Routing.Template.TemplateBinderFactory>();
            return new AspNetCorePipelineExecutor(endpointDataSource, matcherPolicies, endpointSelector, templateBinderFactory);
        })
        .As<IPipelineExecutor>()
        .SingleInstance();
    }

    private static void RegisterPackageManagementHandlers(ContainerBuilder builder)
    {
        builder.RegisterType<LoadPackageHandler>()
            .As<IRequestHandler<LoadPackageCommand, LoadPackageResult>>()
            .InstancePerDependency();

        builder.RegisterType<ListPackagesHandler>()
            .As<IRequestHandler<ListPackagesQuery, ListPackagesResult>>()
            .InstancePerDependency();

        builder.RegisterType<UnloadPackageHandler>()
            .As<IRequestHandler<UnloadPackageCommand, UnloadPackageResult>>()
            .InstancePerDependency();

        // Package feature (bulk data export)
        builder.RegisterType<BulkDataExportFeature>()
            .As<IPackageFeature>()
            .SingleInstance();
    }

    private static void RegisterEventHandlers(ContainerBuilder builder, IConfiguration configuration)
    {
        var terminologyAutoImportEnabled = configuration.GetValue<bool>("Terminology:EnableAutoImport", false);

        // Package loaded notification handler
        builder.RegisterType<Ignixa.Application.Events.Package.PackageLoadedNotificationHandler>()
            .As<INotificationHandler<Ignixa.Application.Events.Package.IPackageLoaded>>()
            .InstancePerDependency();

        // Package loaded map cache invalidation
        builder.RegisterType<Ignixa.Application.Operations.Events.PackageLoadedMapCacheInvalidationHandler>()
            .As<INotificationHandler<Ignixa.Application.Events.Package.PackageLoadedEvent>>()
            .InstancePerLifetimeScope();

        // Search parameter sync handler
        builder.RegisterType<Ignixa.DataLayer.SqlEntityFramework.Events.PackageLoadedSearchParameterSyncHandler>()
            .As<INotificationHandler<Ignixa.Application.Events.Package.PackageLoadedEvent>>()
            .InstancePerDependency();

        // Terminology import handler (conditional on config)
        if (terminologyAutoImportEnabled)
        {
            builder.RegisterType<Ignixa.DataLayer.SqlEntityFramework.Events.PackageLoadedTerminologyImportHandler>()
                .As<INotificationHandler<Ignixa.Application.Events.Package.PackageLoadedEvent>>()
                .InstancePerDependency();
        }

        // Terminology import triggered handler
        builder.RegisterType<Ignixa.Application.BackgroundOperations.Terminology.EventHandlers.TerminologyImportTriggeredHandler>()
            .As<INotificationHandler<Ignixa.Application.Events.Terminology.TerminologyImportTriggeredEvent>>()
            .InstancePerDependency();
    }

    /// <summary>
    /// Registers DurableTask activities and orchestrations.
    /// </summary>
    public static ContainerBuilder RegisterDurableTaskActivities(this ContainerBuilder builder)
    {
        var backgroundOpsAssembly = typeof(Ignixa.Application.BackgroundOperations.Export.Activities.ExportWorkerActivity).Assembly;

        // Register all TaskActivity types
        builder.RegisterAssemblyTypes(backgroundOpsAssembly)
            .Where(t => typeof(TaskActivity).IsAssignableFrom(t) && !t.IsAbstract)
            .AsSelf()
            .InstancePerDependency();

        // Register all TaskOrchestration types
        builder.RegisterAssemblyTypes(backgroundOpsAssembly)
            .Where(t => typeof(TaskOrchestration).IsAssignableFrom(t) && !t.IsAbstract)
            .AsSelf()
            .InstancePerDependency();

        return builder;
    }

    private static void RegisterAuthorizationServices(ContainerBuilder builder)
    {
        // Authorization handlers
        builder.RegisterType<Ignixa.Application.Features.Authorization.Handlers.AuthenticationHandler>()
            .As<Ignixa.Application.Features.Authorization.Handlers.IAuthorizationHandler>()
            .InstancePerLifetimeScope();

        builder.RegisterType<Ignixa.Application.Features.Authorization.Handlers.TenantIsolationHandler>()
            .As<Ignixa.Application.Features.Authorization.Handlers.IAuthorizationHandler>()
            .InstancePerLifetimeScope();

        builder.RegisterType<Ignixa.Application.Features.Authorization.Handlers.RbacAuthorizationHandler>()
            .As<Ignixa.Application.Features.Authorization.Handlers.IAuthorizationHandler>()
            .InstancePerLifetimeScope();

        builder.RegisterType<Ignixa.Application.Features.Authorization.Handlers.SmartScopeAuthorizationHandler>()
            .As<Ignixa.Application.Features.Authorization.Handlers.IAuthorizationHandler>()
            .InstancePerLifetimeScope();

        // Role permission store
        builder.RegisterType<Ignixa.Application.Features.Authorization.Handlers.InMemoryRolePermissionStore>()
            .As<Ignixa.Application.Features.Authorization.Handlers.IRolePermissionStore>()
            .SingleInstance();

        // Authorization service
        builder.RegisterType<Ignixa.Application.Features.Authorization.Services.FhirAuthorizationService>()
            .As<Ignixa.Application.Features.Authorization.Services.IFhirAuthorizationService>()
            .InstancePerLifetimeScope();

        // MCP authorization service
        builder.RegisterType<Ignixa.Application.Features.Mcp.Authorization.McpAuthorizationService>()
            .As<Ignixa.Application.Features.Mcp.Authorization.IMcpAuthorizationService>()
            .InstancePerLifetimeScope();

        // SMART configuration provider
        builder.RegisterType<Ignixa.Application.Features.Authorization.Services.OidcDiscoverySmartConfigurationProvider>()
            .As<Ignixa.Application.Features.Authorization.Services.ISmartConfigurationProvider>()
            .InstancePerLifetimeScope();
    }
}
