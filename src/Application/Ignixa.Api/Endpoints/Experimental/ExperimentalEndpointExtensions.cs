// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;

namespace Ignixa.Api.Endpoints.Experimental;

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
    /// <param name="configureTenantGroup">
    /// Optional delegate to configure the tenant route group (e.g., add filters).
    /// Called by the Api layer to apply FhirAuthorizationFilter, FhirAuditFilter, etc.
    /// </param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapExperimentalEndpoints(
        this WebApplication app,
        IConfiguration configuration,
        Action<RouteGroupBuilder>? configureTenantGroup = null)
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
            app.MapTransformEndpoints(configureTenantGroup);
        }

        // Feature: Terminology - $expand, $translate, $subsumes
        if (options.Features.Terminology.Enabled)
        {
            app.MapTerminologyEndpoints(configureTenantGroup);
        }

        // Feature: Summary - $summary (IPS) operation
        if (options.Features.Summary.Enabled)
        {
            app.MapSummaryEndpoints(configureTenantGroup);
        }

        // Feature: GraphQL - $graphql operation
        if (options.Features.GraphQl.Enabled)
        {
            app.MapGraphQlEndpoints(configureTenantGroup);

            if (options.Features.GraphQl.EnableGraphQlIde)
            {
                var deploymentVersion = options.Features.GraphQl.WarmupVersions.FirstOrDefault(FhirVersion.R4);
                var schemaName = GraphQlNamingHelper.GetSchemaName(deploymentVersion);

                var ideGroup = app.MapGroup("/graphql");
                configureTenantGroup?.Invoke(ideGroup);
                ideGroup.MapGraphQL(path: "/", schemaName: schemaName);
            }
        }

        return app;
    }
}
