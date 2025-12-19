// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Experimental.Configuration;
using Ignixa.Application.Experimental.Endpoints;

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
            app.MapMcpEndpoints();
        }

        // Feature: Transform - $transform operation
        if (options.Features.Transform.Enabled)
        {
            app.MapTransformEndpoints();
        }

        // Feature: Terminology - $expand, $translate, $subsumes
        if (options.Features.Terminology.Enabled)
        {
            app.MapTerminologyEndpoints();
        }

        return app;
    }
}
