// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Endpoints;
using Ignixa.Api.Filters;
using Ignixa.Api.Endpoints.Experimental;

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

        // FHIR operation endpoints ($validate, $de-identify, etc.)
        app.MapOperationEndpoints();
        app.MapDeIdOperationEndpoints();

        // PATCH endpoints (direct and conditional)
        app.MapPatchEndpoints();

        // Compartment search endpoints (GET /Patient/123/Observation)
        app.MapCompartmentEndpoints();

        // Metadata endpoints (CapabilityStatement)
        app.MapMetadataEndpoints();

        // SMART on FHIR discovery endpoints (/.well-known/smart-configuration)
        app.MapSmartDiscoveryEndpoints();

        // Experimental endpoints (MCP, Transform, Terminology)
        // Controlled by Experimental:Enabled and per-feature configuration
        // Apply standard FHIR filters to experimental tenant endpoints
        app.MapExperimentalEndpoints(configuration, ConfigureExperimentalTenantGroup);

        return app;
    }

    /// <summary>
    /// Configures the experimental tenant route group with standard FHIR filters.
    /// This ensures experimental endpoints have the same authorization, audit, metrics,
    /// and resource type validation as core FHIR endpoints.
    /// </summary>
    private static void ConfigureExperimentalTenantGroup(RouteGroupBuilder tenantGroup)
    {
        tenantGroup
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>()
            .AddEndpointFilter<ResourceTypeValidationFilter>();
    }
}
