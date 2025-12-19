// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Application.Experimental.Configuration;
using Ignixa.Application.Experimental.Features.Mcp.Authorization;
using Ignixa.Application.Experimental.Features.Terminology.Expand;
using Ignixa.Application.Experimental.Features.Terminology.Subsumes;
using Ignixa.Application.Experimental.Features.Terminology.Translate;
using Ignixa.Application.Experimental.Features.Transform;
using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Application.Experimental.Infrastructure;

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

        // Register supporting services
        builder.RegisterType<MapRegistryCache>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FhirPathExpressionCache>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConceptMapResolverService>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<FhirPathEvaluatorWithTimeout>()
            .AsSelf()
            .InstancePerLifetimeScope();
    }

    private static void RegisterTerminologyHandlers(this ContainerBuilder builder)
    {
        // Register Terminology handlers
        builder.RegisterType<ExpandValueSetHandler>()
            .As<IRequestHandler<ExpandValueSetQuery, ExpandValueSetResult>>()
            .InstancePerLifetimeScope();

        builder.RegisterType<TranslateCodeHandler>()
            .As<IRequestHandler<TranslateCodeCommand, TranslateCodeResult>>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SubsumesHandler>()
            .As<IRequestHandler<SubsumesQuery, SubsumesQueryResult>>()
            .InstancePerLifetimeScope();
    }
}
