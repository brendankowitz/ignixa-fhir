// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Http;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.Api.Middleware;

/// <summary>
/// Middleware that extracts tenant ID from the route and validates the tenant exists and is active.
/// Stores tenant context in HttpContext.Items for downstream handlers.
///
/// Route Formats:
/// - Explicit: /tenant/{tenantId:int}/{resourceType}/{id?}
/// - Agnostic: /{resourceType}/{id?} (auto-detects single tenant)
///
/// Responsibilities:
/// 1. Extract tenantId from route parameters OR auto-detect single tenant
/// 2. Validate tenant exists and is active via ITenantConfigurationStore
/// 3. Store tenantId and configuration in HttpContext.Items
/// 4. Return 404 if tenant not found or inactive
/// 5. Return 400 if agnostic route used in multi-tenant scenario
/// </summary>
public class TenantResolutionMiddleware : IDisposable
{
    private readonly RequestDelegate _next;
    private readonly ITenantConfigurationStore _configStore;
    private readonly ILogger<TenantResolutionMiddleware> _logger;
    private int? _cachedSingleTenantId;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _disposed;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ITenantConfigurationStore configStore,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract tenantId from route
        if (context.Request.RouteValues.TryGetValue("tenantId", out var tenantIdObj) &&
            int.TryParse(tenantIdObj?.ToString(), out var tenantId))
        {
            _logger.LogTrace("Extracted tenantId {TenantId} from route", tenantId);

            // CRITICAL: Partition 0 is reserved for system operations (ADR-2523 Phase 20)
            // Regular API requests to /tenant/0/ routes are rejected to protect system partition
            if (tenantId == SystemConstants.SystemPartitionId)
            {
                _logger.LogWarning(
                    "Rejected request to system partition {TenantId} for request {Method} {Path}",
                    tenantId,
                    context.Request.Method,
                    context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = KnownContentTypes.ApplicationJson;
                await context.Response.WriteAsJsonAsync(new
                {
                    resourceType = "OperationOutcome",
                    issue = new[]
                    {
                        new
                        {
                            severity = "error",
                            code = "business-rule",
                            diagnostics = "Partition 0 is reserved for system operations and cannot be accessed via tenant API routes"
                        }
                    }
                }, context.RequestAborted);
                return;
            }

            // Verify tenant exists and is active
            var tenantConfig = await _configStore.GetTenantConfigurationAsync(
                tenantId,
                context.RequestAborted);

            if (tenantConfig == null)
            {
                _logger.LogWarning(
                    "Tenant {TenantId} not found or inactive for request {Method} {Path}",
                    tenantId,
                    context.Request.Method,
                    context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = KnownContentTypes.ApplicationJson;
                await context.Response.WriteAsJsonAsync(new
                {
                    resourceType = "OperationOutcome",
                    issue = new[]
                    {
                        new
                        {
                            severity = "error",
                            code = "not-found",
                            diagnostics = $"Tenant {tenantId} not found or inactive"
                        }
                    }
                }, context.RequestAborted);
                return;
            }

            // Store tenant context in HttpContext.Items for downstream handlers
            context.Items["TenantId"] = tenantId;
            context.Items["TenantConfiguration"] = tenantConfig;

            _logger.LogDebug(
                "Resolved tenant {TenantId} ({DisplayName}) for request {Method} {Path}",
                tenantId,
                tenantConfig.DisplayName,
                context.Request.Method,
                context.Request.Path);
        }
        else if (IsResourceEndpoint(context))
        {
            // No tenantId in route - check if this is a resource endpoint requiring tenant-agnostic routing
            _logger.LogTrace("No tenantId in route, attempting auto-detect for {Method} {Path}", context.Request.Method, context.Request.Path);

            var defaultTenantId = await GetSingleTenantIdAsync(context.RequestAborted);

            if (defaultTenantId.HasValue)
            {
                // Auto-detected single tenant - use it as default
                var tenantConfig = await _configStore.GetTenantConfigurationAsync(
                    defaultTenantId.Value,
                    context.RequestAborted);

                if (tenantConfig != null)
                {
                    context.Items["TenantId"] = defaultTenantId.Value;
                    context.Items["TenantConfiguration"] = tenantConfig;
                    context.Items["IsAgnosticRoute"] = true; // Flag for logging/tracking

                    _logger.LogDebug(
                        "Auto-detected single tenant {TenantId} ({DisplayName}) for agnostic route {Method} {Path}",
                        defaultTenantId.Value,
                        tenantConfig.DisplayName,
                        context.Request.Method,
                        context.Request.Path);
                }
            }
            else
            {
                // Multiple tenants exist - agnostic route is ambiguous
                _logger.LogWarning(
                    "Tenant-agnostic route {Method} {Path} used in multi-tenant scenario (requires explicit /tenant/{{id}}/ prefix)",
                    context.Request.Method,
                    context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = KnownContentTypes.ApplicationJson;
                await context.Response.WriteAsJsonAsync(new
                {
                    resourceType = "OperationOutcome",
                    issue = new[]
                    {
                        new
                        {
                            severity = "error",
                            code = "required",
                            diagnostics = "Tenant ID is required in multi-tenant scenarios. Use /tenant/{tenantId}/" + context.Request.Path.Value
                        }
                    }
                }, context.RequestAborted);
                return;
            }
        }
        else
        {
            // No tenantId in route and not a resource endpoint - this is expected for /metadata, /health, etc.
            _logger.LogTrace("No tenantId found in route for non-resource endpoint {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        await _next(context);
    }

    /// <summary>
    /// Determines if the current request is a FHIR resource endpoint (vs metadata, health check, etc.)
    /// </summary>
    private static bool IsResourceEndpoint(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            // Root path could be bundle POST
            return context.Request.Method == "POST";
        }

        // Exclude known non-resource endpoints
        if (path.StartsWith("/metadata", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Resource endpoints match pattern: /{resourceType} or /{resourceType}/{id}
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 1;
    }

    /// <summary>
    /// Gets the single tenant ID if exactly one active tenant is configured (excluding system partition).
    /// Returns null if zero or multiple active tenants exist.
    /// Result is cached to avoid repeated queries.
    /// </summary>
    private async ValueTask<int?> GetSingleTenantIdAsync(CancellationToken cancellationToken)
    {
        // Check cache first (avoid lock if already cached)
        if (_cachedSingleTenantId.HasValue || _cachedSingleTenantId == -1)
        {
            return _cachedSingleTenantId == -1 ? null : _cachedSingleTenantId;
        }

        // Acquire lock to prevent multiple threads from querying simultaneously
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedSingleTenantId.HasValue || _cachedSingleTenantId == -1)
            {
                return _cachedSingleTenantId == -1 ? null : _cachedSingleTenantId;
            }

            // Query all active tenants (excludes system partition)
            var allTenants = await _configStore.GetAllTenantsAsync(cancellationToken);

            if (allTenants.Count == 1)
            {
                _cachedSingleTenantId = allTenants[0].TenantId;
                _logger.LogInformation(
                    "Single tenant detected: TenantId={TenantId}, DisplayName={DisplayName}. Enabling tenant-agnostic routes (/{{resourceType}}/{{id}}).",
                    _cachedSingleTenantId.Value,
                    allTenants[0].DisplayName);
                return _cachedSingleTenantId;
            }
            else
            {
                // Cache -1 to indicate "no single tenant" (avoids repeated queries)
                _cachedSingleTenantId = -1;
                _logger.LogInformation(
                    "Multiple tenants detected ({Count} active tenants). Tenant-agnostic routes disabled - explicit /tenant/{{id}}/ prefix required.",
                    allTenants.Count);
                return null;
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Dispose of managed resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dispose of managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cacheLock?.Dispose();
        }

        _disposed = true;
    }
}
