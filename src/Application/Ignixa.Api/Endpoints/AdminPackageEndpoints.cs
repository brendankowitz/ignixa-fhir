using Ignixa.Application.Features.Admin;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for package management.
/// Provides endpoints to load, list, and unload FHIR packages (Implementation Guides).
/// </summary>
public static class AdminPackageEndpoints
{
    /// <summary>
    /// Maps admin package management endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminPackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /tenant/{tenantId}/admin/packages/load - Load a package into tenant
        endpoints.MapPost("/tenant/{tenantId}/admin/packages/load", HandleLoadPackage)
            .WithName("LoadPackage")
            .WithDescription("Load a FHIR package from NPM registry into a tenant's database")
            .Accepts<LoadPackageRequest>("application/json")
            .Produces<LoadPackageResult>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

        // GET /tenant/{tenantId}/admin/packages - List packages loaded in tenant
        endpoints.MapGet("/tenant/{tenantId}/admin/packages", HandleListPackages)
            .WithName("ListPackages")
            .WithDescription("List all loaded FHIR packages for a specific tenant")
            .Produces<ListPackagesResponse>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

        // DELETE /tenant/{tenantId}/admin/packages/{packageId}/{version} - Unload package from tenant
        endpoints.MapDelete("/tenant/{tenantId}/admin/packages/{packageId}/{version}", HandleUnloadPackage)
            .WithName("UnloadPackage")
            .WithDescription("Unload (deactivate) a FHIR package from a tenant's database")
            .Produces<UnloadPackageResponse>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

        return endpoints;
    }

    /// <summary>
    /// POST /tenant/{tenantId}/admin/packages/load
    /// Loads a FHIR package from the NPM registry into a tenant's database.
    /// </summary>
    private static async Task<IResult> HandleLoadPackage(
        string tenantId,
        [FromBody] LoadPackageRequest request,
        [FromServices] IMediator mediator,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(AdminPackageEndpoints).FullName!);
        logger.LogInformation("POST /tenant/{TenantId}/admin/packages/load - {PackageId}@{Version}", tenantId, request.PackageId, request.Version);

        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.BadRequest("Tenant ID is required");
            if (string.IsNullOrWhiteSpace(request.PackageId))
                return Results.BadRequest("Package ID is required");
            if (string.IsNullOrWhiteSpace(request.Version))
                return Results.BadRequest("Version is required");

            // Execute command via Medino
            var command = new LoadPackageCommand(tenantId, request.PackageId, request.Version);
            var result = await mediator.SendAsync(command, cancellationToken);

            logger.LogInformation(
                "Package {PackageId}@{Version} loaded successfully. {Count} resources imported",
                result.PackageId, result.PackageVersion, result.ImportedResources);

            var response = new LoadPackageResponse
            {
                PackageId = result.PackageId,
                PackageVersion = result.PackageVersion,
                TotalResources = result.TotalResources,
                ImportedResources = result.ImportedResources,
                DurationMilliseconds = result.DurationMilliseconds,
                ResourcesByType = result.ResourcesByType
            };

            return Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid request for LoadPackage");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Package not found");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error loading package");
            return Results.Json(
                new { error = "Failed to load package", details = ex.Message },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// GET /tenant/{tenantId}/admin/packages
    /// Lists all loaded FHIR packages for a specific tenant.
    /// </summary>
    private static async Task<IResult> HandleListPackages(
        string tenantId,
        [FromServices] IMediator mediator,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(AdminPackageEndpoints).FullName!);
        logger.LogDebug("GET /tenant/{TenantId}/admin/packages", tenantId);

        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.BadRequest("Tenant ID is required");

            // Execute query via Medino
            var query = new ListPackagesQuery(tenantId);
            var result = await mediator.SendAsync(query, cancellationToken);

            logger.LogInformation("Listed {Count} loaded packages", result.Packages.Count);

            var response = new ListPackagesResponse
            {
                Packages = result.Packages,
                Count = result.Packages.Count
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error listing packages");
            return Results.Json(
                new { error = "Failed to list packages", details = ex.Message },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// DELETE /tenant/{tenantId}/admin/packages/{packageId}/{version}
    /// Unloads (deactivates) a FHIR package from a tenant's database.
    /// </summary>
    private static async Task<IResult> HandleUnloadPackage(
        string tenantId,
        string packageId,
        string version,
        [FromServices] IMediator mediator,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(AdminPackageEndpoints).FullName!);
        logger.LogInformation("DELETE /tenant/{TenantId}/admin/packages/{PackageId}/{Version}", tenantId, packageId, version);

        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.BadRequest("Tenant ID is required");
            if (string.IsNullOrWhiteSpace(packageId))
                return Results.BadRequest("Package ID is required");
            if (string.IsNullOrWhiteSpace(version))
                return Results.BadRequest("Version is required");

            // Execute command via Medino
            var command = new UnloadPackageCommand(tenantId, packageId, version);
            var result = await mediator.SendAsync(command, cancellationToken);

            if (result.ResourcesDeactivated == 0)
            {
                logger.LogWarning("Package not found: {PackageId}@{Version}", packageId, version);
                return Results.NotFound(new { error = $"Package '{packageId}@{version}' not found" });
            }

            logger.LogInformation(
                "Package {PackageId}@{Version} unloaded. {Count} resources deactivated",
                result.PackageId, result.Version, result.ResourcesDeactivated);

            var response = new UnloadPackageResponse
            {
                PackageId = result.PackageId,
                Version = result.Version,
                ResourcesDeactivated = result.ResourcesDeactivated
            };

            return Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid request for UnloadPackage");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error unloading package {PackageId}@{Version}", packageId, version);
            return Results.Json(
                new { error = "Failed to unload package", details = ex.Message },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Request/Response DTOs

    /// <summary>
    /// Request to load a package.
    /// </summary>
    public record LoadPackageRequest
    {
        /// <summary>
        /// Package ID (e.g., "hl7.fhir.us.core").
        /// </summary>
        public required string PackageId { get; init; }

        /// <summary>
        /// Package version (e.g., "5.0.1").
        /// </summary>
        public required string Version { get; init; }
    }

    /// <summary>
    /// Response from loading a package.
    /// </summary>
    public record LoadPackageResponse
    {
        /// <summary>
        /// Package ID.
        /// </summary>
        public required string PackageId { get; init; }

        /// <summary>
        /// Package version.
        /// </summary>
        public required string PackageVersion { get; init; }

        /// <summary>
        /// Total resources extracted.
        /// </summary>
        public int TotalResources { get; init; }

        /// <summary>
        /// Resources imported.
        /// </summary>
        public int ImportedResources { get; init; }

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public long DurationMilliseconds { get; init; }

        /// <summary>
        /// Breakdown by resource type.
        /// </summary>
        public Dictionary<string, int> ResourcesByType { get; init; } = new();
    }

    /// <summary>
    /// Response from listing packages.
    /// </summary>
    public record ListPackagesResponse
    {
        /// <summary>
        /// List of packages.
        /// </summary>
        public required IReadOnlyList<PackageInfo> Packages { get; init; }

        /// <summary>
        /// Total count.
        /// </summary>
        public int Count { get; init; }
    }

    /// <summary>
    /// Response from unloading a package.
    /// </summary>
    public record UnloadPackageResponse
    {
        /// <summary>
        /// Package ID.
        /// </summary>
        public required string PackageId { get; init; }

        /// <summary>
        /// Package version.
        /// </summary>
        public required string Version { get; init; }

        /// <summary>
        /// Number of resources deactivated.
        /// </summary>
        public int ResourcesDeactivated { get; init; }
    }
}
