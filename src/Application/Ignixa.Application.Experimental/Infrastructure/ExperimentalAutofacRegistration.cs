// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Application.Experimental.Configuration;

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
        // MCP handlers are already registered in the main application
        // This is a placeholder for future MCP-specific handler registrations
        // when MCP is fully migrated to the experimental library
    }

    private static void RegisterTransformHandlers(this ContainerBuilder builder)
    {
        // Transform handlers are already registered in the main application
        // This is a placeholder for future Transform-specific handler registrations
        // when Transform is fully migrated to the experimental library
    }

    private static void RegisterTerminologyHandlers(this ContainerBuilder builder)
    {
        // Terminology handlers are already registered in the main application
        // This is a placeholder for future Terminology-specific handler registrations
        // when Terminology is fully migrated to the experimental library
    }
}
