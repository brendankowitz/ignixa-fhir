// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using ISchema = Ignixa.Abstractions.ISchema;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.DataLoaders;
using Ignixa.Application.Features.Experimental.GraphQl.Events;
using Ignixa.Application.Features.Experimental.GraphQl.Execution;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Application.Features.Experimental.Ips.Events;
using Ignixa.Application.Features.Experimental.Ips.Generator;
using Ignixa.Application.Features.Experimental.Ips.Strategy;
using Ignixa.Application.Features.Experimental.Mcp.Authorization;
using Ignixa.Application.Features.Experimental.Terminology.Expand;
using Ignixa.Application.Features.Experimental.Terminology.Subsumes;
using Ignixa.Application.Features.Experimental.Terminology.Translate;
using Ignixa.Application.Features.Experimental.Transform;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Experimental.Transform.Events;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.NarrativeGenerator;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Infrastructure;

/// <summary>
/// Extension methods for registering experimental services with Autofac ContainerBuilder.
/// </summary>
public static class ExperimentalAutofacRegistration
{
    /// <summary>
    /// Registers experimental services with the Autofac container.
    /// Respects the master switch and per-feature configuration.
    /// </summary>
    /// <param name="builder">The Autofac container builder.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The container builder for chaining.</returns>
    public static ContainerBuilder RegisterExperimentalServices(
        this ContainerBuilder builder,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(ExperimentalOptions.SectionName)
            .Get<ExperimentalOptions>() ?? new ExperimentalOptions();

        // Master switch check - if disabled, return early
        if (!options.Enabled)
        {
            return builder;
        }

        // Feature: MCP - Model Context Protocol
        if (options.Features.Mcp.Enabled)
        {
            builder.RegisterMcpHandlers();
        }

        // Feature: Transform - FHIR Mapping Language
        if (options.Features.Transform.Enabled)
        {
            builder.RegisterTransformHandlers();
        }

        // Feature: Terminology - $expand, $translate, $subsumes
        if (options.Features.Terminology.Enabled)
        {
            builder.RegisterTerminologyHandlers();
        }

        // Feature: Summary - Patient $summary (IPS)
        if (options.Features.Summary.Enabled)
        {
            builder.RegisterIpsHandlers();
        }

        // Feature: GraphQL - $graphql operation
        if (options.Features.GraphQl.Enabled)
        {
            builder.RegisterGraphQlHandlers();

            // Advertise $graphql in CapabilityStatement when GraphQL is enabled
            builder.RegisterType<GraphQlFeature>()
                .As<IPackageFeature>()
                .SingleInstance();
        }

        return builder;
    }

    private static void RegisterMcpHandlers(this ContainerBuilder builder)
    {
        // Register MCP Authorization service
        builder.RegisterType<McpAuthorizationService>()
            .As<IMcpAuthorizationService>()
            .InstancePerLifetimeScope();

        // Note: MCP tools are registered automatically by ModelContextProtocol.AspNetCore
        // via assembly scanning when endpoints are mapped
    }

    private static void RegisterTransformHandlers(this ContainerBuilder builder)
    {
        // Register Transform handler
        builder.RegisterType<TransformResourceHandler>()
            .As<IRequestHandler<TransformResourceCommand, ResourceJsonNode>>()
            .InstancePerLifetimeScope();

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
        builder.RegisterType<MapRegistryCache>()
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

        // ConceptMap translation service
        builder.RegisterType<ConceptMapResolverService>()
            .AsSelf()
            .InstancePerLifetimeScope();

        // Package loaded event handler to invalidate map cache
        builder.RegisterType<PackageLoadedMapCacheInvalidationHandler>()
            .As<INotificationHandler<PackageLoadedEvent>>()
            .InstancePerDependency();
    }

    private static void RegisterTerminologyHandlers(this ContainerBuilder builder)
    {
        // Register Terminology handlers
        builder.RegisterType<ExpandValueSetHandler>()
            .As<IRequestHandler<ExpandValueSetQuery, ExpandValueSetResult>>()
            .InstancePerDependency();

        builder.RegisterType<TranslateCodeHandler>()
            .As<IRequestHandler<TranslateCodeCommand, TranslateCodeResult>>()
            .InstancePerDependency();

        builder.RegisterType<SubsumesHandler>()
            .As<IRequestHandler<SubsumesQuery, SubsumesQueryResult>>()
            .InstancePerDependency();
    }

    private static void RegisterIpsHandlers(this ContainerBuilder builder)
    {
        // NOTE: INarrativeGenerator is registered in ApplicationServicesRegistration.RegisterNarrativeServices()
        // as it's a general-purpose service used by multiple features

        // IPS Generation Strategy (singleton - stateless configuration)
        builder.RegisterType<DefaultIpsGenerationStrategy>()
            .As<IIpsGenerationStrategy>()
            .SingleInstance();

        // IPS Generation Strategy Registry (singleton)
        builder.RegisterType<Ignixa.Application.Features.Experimental.Ips.Registry.IpsGenerationStrategyRegistry>()
            .As<IIpsGenerationStrategyRegistry>()
            .SingleInstance();

        // IPS Generation Strategy Factory (singleton - stateless parser)
        builder.RegisterType<StructureDefinitionStrategyFactory>()
            .As<IStructureDefinitionStrategyFactory>()
            .SingleInstance();

        // ISchema (request-scoped, tenant-aware)
        builder.Register(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            var requestContextAccessor = c.Resolve<IFhirRequestContextAccessor>();

            var requestContext = requestContextAccessor.RequestContext;
            return requestContext is not null
                ? versionContext.GetSchemaProvider(requestContext.FhirVersion, requestContext.TenantId)
                : versionContext.GetBaseSchemaProvider(FhirVersion.R4);
        })
        .As<ISchema>()
        .InstancePerLifetimeScope();

        // IPS Generator Service (scoped per request - uses request context)
        builder.RegisterType<IpsGeneratorService>()
            .As<IIpsGeneratorService>()
            .InstancePerLifetimeScope();

        // IPS Generator Handler
        builder.RegisterType<IpsGeneratorHandler>()
            .As<IRequestHandler<IpsGeneratorQuery, IpsGeneratorResult>>()
            .InstancePerDependency();

        // Package loaded event handler to register IPS strategies
        builder.RegisterType<PackageInstalledStrategyRegistrationHandler>()
            .As<INotificationHandler<PackageLoadedEvent>>()
            .InstancePerDependency();
    }

    private static void RegisterGraphQlHandlers(this ContainerBuilder builder)
    {
        builder.RegisterType<ResourceResolver>()
            .InstancePerLifetimeScope();

        builder.RegisterType<MutationResolver>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SearchResolver>()
            .InstancePerLifetimeScope();

        builder.RegisterType<ResourceDataLoader>()
            .InstancePerLifetimeScope();

        builder.RegisterType<GraphQlExecutionService>()
            .As<IGraphQlExecutionService>()
            .InstancePerLifetimeScope();

        builder.Register(c =>
        {
            var sp = c.Resolve<IServiceProvider>();
            var modules = new List<IFhirTypeModule>();
            foreach (var version in GraphQlNamingHelper.SupportedVersions)
            {
                var module = sp.GetKeyedService<IFhirTypeModule>(version);
                if (module is not null)
                    modules.Add(module);
            }
            return (IReadOnlyList<IFhirTypeModule>)modules;
        }).SingleInstance();

        builder.RegisterType<PackageLoadedSchemaInvalidationHandler>()
            .As<INotificationHandler<PackageLoadedEvent>>()
            .InstancePerDependency();
    }
}
