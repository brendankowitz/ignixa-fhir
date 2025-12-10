// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Endpoints;

namespace Ignixa.Api.Extensions;

/// <summary>
/// Provides extension methods for mapping all Ignixa FHIR endpoints.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps all Ignixa FHIR server endpoints including CRUD, search, operations,
    /// history, patch, compartment, and admin endpoints.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapIgnixaEndpoints(
        this WebApplication app,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Health check endpoints (before FHIR endpoints, bypasses tenant resolution)
        app.MapHealthCheckEndpoints();

        // Bulk operations BEFORE generic FHIR endpoints
        // This ensures /$import and /$export routes match before the generic /{resourceType} catch-all
        app.MapExportEndpoints();
        app.MapImportEndpoints();

        // Admin package management endpoints
        app.MapAdminPackageEndpoints();

        // Core FHIR endpoints (CRUD, search, bundles)
        app.MapFhirEndpoints();

        // FHIR _history endpoints
        app.MapFhirHistoryEndpoints();

        // FHIR operation endpoints ($validate, etc.)
        app.MapOperationEndpoints();

        // Terminology endpoints ($expand, $translate, $subsumes)
        app.MapTerminologyEndpoints();

        // PATCH endpoints (direct and conditional)
        app.MapPatchEndpoints();

        // Compartment search endpoints (GET /Patient/123/Observation)
        app.MapCompartmentEndpoints();

        // Metadata endpoints (CapabilityStatement)
        app.MapMetadataEndpoints();

        // MCP endpoints (conditional)
        var mcpEnabled = configuration.GetValue<bool>("Mcp:Enabled", true);
        if (mcpEnabled)
        {
            app.MapMcpEndpoints();
        }

        return app;
    }
}
