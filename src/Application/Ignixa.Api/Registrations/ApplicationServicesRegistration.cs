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
using Ignixa.Application.Operations.Features.Validate;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirMappingLanguage.Mutator;
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

        // NOTE: FML services are now registered by ExperimentalAutofacRegistration.RegisterTransformHandlers()

        // CapabilityStatement services
        RegisterCapabilityServices(builder);

        // Bundle processing services
        RegisterBundleServices(builder);

        // Package management handlers
        RegisterPackageManagementHandlers(builder);

        // Event handlers
        RegisterEventHandlers(builder, configuration);

        // Sidecar configuration options
        builder.Register(c =>
        {
            var config = c.Resolve<IConfiguration>();
            var options = new Ignixa.Application.Infrastructure.SidecarOptions();
            config.GetSection(Ignixa.Application.Infrastructure.SidecarOptions.SectionName).Bind(options);
            return options;
        }).As<Ignixa.Application.Infrastructure.SidecarOptions>().SingleInstance();

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

        // NOTE: Terminology ($expand, $translate, $subsumes) and $transform handlers
        // are now registered by ExperimentalAutofacRegistration.RegisterExperimentalServices()

        // Patient $everything
        builder.RegisterType<PatientEverythingHandler>()
            .As<IRequestHandler<PatientEverythingQuery, SearchResourcesResult>>()
            .InstancePerDependency();

        // $member-match
        builder.RegisterType<Ignixa.Application.Operations.Features.MemberMatch.MemberMatchHandler>()
            .As<IRequestHandler<Ignixa.Application.Operations.Features.MemberMatch.MemberMatchCommand, Ignixa.Application.Operations.Features.MemberMatch.MemberMatchResult>>()
            .InstancePerDependency();

        builder.RegisterType<Ignixa.Application.Operations.Features.MemberMatch.DefaultMemberMatchStrategy>()
            .As<Ignixa.Application.Operations.Features.MemberMatch.IMemberMatchStrategy>()
            .InstancePerDependency();
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

    // NOTE: RegisterFmlServices removed - now in ExperimentalAutofacRegistration.RegisterTransformHandlers()

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
        var terminologyAutoImportEnabled = configuration.GetValue<bool>("Experimental:Features:Terminology:EnableAutoImport", false);

        // Package loaded notification handler
        builder.RegisterType<Ignixa.Application.Events.Package.PackageLoadedNotificationHandler>()
            .As<INotificationHandler<Ignixa.Application.Events.Package.IPackageLoaded>>()
            .InstancePerDependency();

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

        // RBAC handler: Conditional registration based on Sidecar.Enabled
        builder.Register<Ignixa.Application.Features.Authorization.Handlers.IAuthorizationHandler>(c =>
        {
            var sidecarOptions = c.Resolve<Ignixa.Application.Infrastructure.SidecarOptions>();

            if (sidecarOptions.Enabled)
            {
                // Sidecar mode: Use gRPC client
                var client = c.Resolve<Ignixa.Sidecar.Rbac.RbacService.RbacServiceClient>();
                var logger = c.Resolve<ILogger<Ignixa.Application.Features.Authorization.Handlers.SidecarRbacAuthorizationHandler>>();
                return new Ignixa.Application.Features.Authorization.Handlers.SidecarRbacAuthorizationHandler(client, logger);
            }
            else
            {
                // Local mode: Use role permission store
                var store = c.Resolve<Ignixa.Application.Features.Authorization.Handlers.IRolePermissionStore>();
                var logger = c.Resolve<ILogger<Ignixa.Application.Features.Authorization.Handlers.RbacAuthorizationHandler>>();
                return new Ignixa.Application.Features.Authorization.Handlers.RbacAuthorizationHandler(store, logger);
            }
        })
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
        builder.RegisterType<Ignixa.Application.Features.Experimental.Mcp.Authorization.McpAuthorizationService>()
            .As<Ignixa.Application.Features.Experimental.Mcp.Authorization.IMcpAuthorizationService>()
            .InstancePerLifetimeScope();

        // SMART configuration provider
        builder.RegisterType<Ignixa.Application.Features.Authorization.Services.OidcDiscoverySmartConfigurationProvider>()
            .As<Ignixa.Application.Features.Authorization.Services.ISmartConfigurationProvider>()
            .InstancePerLifetimeScope();
    }
}
