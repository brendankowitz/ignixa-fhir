// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Experimental.Configuration;

namespace Ignixa.Application.Experimental.Infrastructure;

/// <summary>
/// Extension methods for registering experimental endpoints with WebApplication.
/// </summary>
public static class ExperimentalEndpointExtensions
{
    /// <summary>
    /// Maps experimental feature endpoints to the application.
    /// Respects the master switch and per-feature configuration.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapExperimentalEndpoints(
        this WebApplication app,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(ExperimentalOptions.SectionName)
            .Get<ExperimentalOptions>() ?? new ExperimentalOptions();

        // Master switch check - if disabled, return early
        if (!options.Enabled)
        {
            return app;
        }

        // Feature: MCP - Model Context Protocol
        if (options.Features.Mcp.Enabled)
        {
            app.MapMcpExperimentalEndpoints();
        }

        // Feature: Transform - $transform operation
        if (options.Features.Transform.Enabled)
        {
            app.MapTransformExperimentalEndpoints();
        }

        // Feature: Terminology - $expand, $translate, $subsumes
        if (options.Features.Terminology.Enabled)
        {
            app.MapTerminologyExperimentalEndpoints();
        }

        return app;
    }

    private static void MapMcpExperimentalEndpoints(this WebApplication app)
    {
        // MCP endpoints are already registered in the main application via McpEndpoints.cs
        // This is a placeholder for future MCP-specific endpoint registrations
        // when MCP is fully migrated to the experimental library
    }

    private static void MapTransformExperimentalEndpoints(this WebApplication app)
    {
        // Transform endpoints are already registered in the main application via OperationEndpoints.cs
        // This is a placeholder for future Transform-specific endpoint registrations
        // when Transform is fully migrated to the experimental library
    }

    private static void MapTerminologyExperimentalEndpoints(this WebApplication app)
    {
        // Terminology endpoints are already registered in the main application via TerminologyEndpoints.cs
        // This is a placeholder for future Terminology-specific endpoint registrations
        // when Terminology is fully migrated to the experimental library
    }
}
