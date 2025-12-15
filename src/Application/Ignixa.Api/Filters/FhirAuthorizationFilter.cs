// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Http;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Features.Authorization.Smart;
using Ignixa.Application.Infrastructure;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.Filters;

/// <summary>
/// Endpoint filter that enforces FHIR authorization (RBAC, SMART scopes, capability enforcement).
/// Runs on EVERY FHIR endpoint, including bundle entry processing.
///
/// Architecture Decision: Endpoint filters are used instead of middleware because:
/// 1. Bundle Processing: Bundle entries bypass middleware but go through endpoint filters
/// 2. Per-Endpoint Granularity: Different endpoints can have different authorization requirements
/// 3. Composition: Stack multiple filters (auth → audit → validation)
/// 4. Type Safety: Access to endpoint metadata and route parameters
///
/// Usage:
///   var group = endpoints.MapGroup("/tenant/{tenantId:int}")
///       .AddEndpointFilter<FhirAuthorizationFilter>()
///       .AddEndpointFilter<FhirAuditFilter>()
///       .AddEndpointFilter<ResourceTypeValidationFilter>();
/// </summary>
public class FhirAuthorizationFilter : IEndpointFilter
{
    private readonly IFhirAuthorizationService _authzService;
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;
    private readonly ILogger<FhirAuthorizationFilter> _logger;
    private readonly AuthorizationOptions _authzOptions;

    public FhirAuthorizationFilter(
        IFhirAuthorizationService authzService,
        IFhirRequestContextAccessor fhirContextAccessor,
        IOptions<AuthorizationOptions> authzOptions,
        ILogger<FhirAuthorizationFilter> logger)
    {
        _authzService = authzService ?? throw new ArgumentNullException(nameof(authzService));
        _fhirContextAccessor = fhirContextAccessor ?? throw new ArgumentNullException(nameof(fhirContextAccessor));
        _authzOptions = authzOptions?.Value ?? throw new ArgumentNullException(nameof(authzOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        // Skip authorization if disabled in configuration
        if (!_authzOptions.Enabled)
        {
            return await next(context);
        }

        var httpContext = context.HttpContext;

        try
        {
            // Build authorization context from route + request + user claims
            var authContext = await BuildAuthorizationContextAsync(httpContext);

            // Run authorization handlers (authentication → RBAC → SMART → capability)
            var result = await _authzService.AuthorizeAsync(authContext);

            if (!result.Allowed)
            {
                _logger.LogWarning(
                    "Authorization denied: {Reason} (User: {User}, Resource: {ResourceType}/{ResourceId}, Interaction: {Interaction})",
                    result.DenialReason,
                    authContext.UserId ?? "anonymous",
                    authContext.ResourceType ?? "system",
                    authContext.ResourceId ?? "none",
                    authContext.Interaction);

                // Return FHIR OperationOutcome with 403 Forbidden
                return CreateForbiddenResponse(result.DenialReason ?? "Access denied");
            }

            // Store filter in HttpContext for query layer (patient compartment filtering)
            if (result.Filter != null)
            {
                httpContext.Items["FhirAuthorizationFilter"] = result.Filter;

                _logger.LogDebug(
                    "Authorization filter applied: PatientFilter={PatientFilter}",
                    result.Filter.PatientFilter ?? "none");
            }

            // Continue to next filter or handler
            return await next(context);
        }
        catch (Exception ex)
        {
            // Sanitize path to prevent log injection (char overload avoids CA1307)
            var safePath = (httpContext.Request.Path.Value ?? "/")
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            _logger.LogError(
                ex,
                "Authorization error for {Path}",
                safePath);

            // Return generic error to prevent internal error leakage
            return CreateErrorResponse("An error occurred during authorization");
        }
    }

    /// <summary>
    /// Creates a 403 Forbidden response with FHIR OperationOutcome.
    /// </summary>
    private static IResult CreateForbiddenResponse(string diagnostics)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Forbidden,
            Diagnostics = diagnostics
        });

        return Results.Content(
            outcome.SerializeToString(),
            KnownContentTypes.ApplicationFhirJson,
            statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Creates a 500 Internal Server Error response with FHIR OperationOutcome.
    /// </summary>
    private static IResult CreateErrorResponse(string diagnostics)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Exception,
            Diagnostics = diagnostics
        });

        return Results.Content(
            outcome.SerializeToString(),
            KnownContentTypes.ApplicationFhirJson,
            statusCode: StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Builds the authorization context from HTTP context.
    /// </summary>
    private Task<FhirAuthorizationContext> BuildAuthorizationContextAsync(HttpContext httpContext)
    {
        // Get FhirRequestContext (set by TenantResolutionMiddleware)
        var fhirContext = _fhirContextAccessor.RequestContext
            ?? throw new InvalidOperationException(
                "FhirRequestContext not set. Ensure TenantResolutionMiddleware runs before authorization.");

        // Extract user/tenant from claims
        var userId = httpContext.User.FindFirst(FhirClaimTypes.Subject)?.Value ??
                    httpContext.User.FindFirst(FhirClaimTypes.ObjectId)?.Value ??
                    httpContext.User.FindFirst(FhirClaimTypes.NameIdentifier)?.Value;

        var roles = httpContext.User.FindAll(FhirClaimTypes.Role)
            .Concat(httpContext.User.FindAll(FhirClaimTypes.Roles))
            .Concat(httpContext.User.FindAll(FhirClaimTypes.WsFederationRole))
            .Select(c => c.Value)
            .Distinct()
            .ToList();

        // Extract SMART context if present
        SmartAuthorizationContext? smartContext = null;
        var scopeClaim = httpContext.User.FindFirst(FhirClaimTypes.Scope)?.Value ??
                        httpContext.User.FindFirst(FhirClaimTypes.Scp)?.Value;

        if (!string.IsNullOrEmpty(scopeClaim))
        {
            var scopes = SmartScopeParser.ParseScopes(scopeClaim);
            if (scopes.Count > 0)
            {
                var patientClaim = httpContext.User.FindFirst(FhirClaimTypes.Patient)?.Value ??
                                  httpContext.User.FindFirst(FhirClaimTypes.LaunchContextPatient)?.Value;

                var encounterClaim = httpContext.User.FindFirst(FhirClaimTypes.Encounter)?.Value ??
                                    httpContext.User.FindFirst(FhirClaimTypes.LaunchContextEncounter)?.Value;

                var fhirUserClaim = httpContext.User.FindFirst(FhirClaimTypes.FhirUser)?.Value;

                var tokenClaims = new SmartTokenClaims
                {
                    ScopeString = scopeClaim,
                    Scopes = scopes,
                    PatientId = patientClaim,
                    EncounterId = encounterClaim,
                    FhirUser = fhirUserClaim,
                    ClientId = httpContext.User.FindFirst(FhirClaimTypes.ClientId)?.Value ??
                              httpContext.User.FindFirst(FhirClaimTypes.AuthorizedParty)?.Value
                };

                smartContext = new SmartAuthorizationContext
                {
                    TokenClaims = tokenClaims,
                    Scopes = scopes,
                    PatientContext = patientClaim,
                    EncounterContext = encounterClaim,
                    UserContext = fhirUserClaim
                };
            }
        }

        // Parse route to determine interaction
        var (interaction, resourceType, resourceId) = ParseRoute(httpContext);

        var authContext = new FhirAuthorizationContext
        {
            RequestContext = fhirContext,
            UserId = userId,
            Roles = roles.Count > 0 ? roles : null,
            SmartContext = smartContext,
            Interaction = interaction,
            ResourceType = resourceType,
            ResourceId = resourceId,
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        return Task.FromResult(authContext);
    }

    /// <summary>
    /// Parses HTTP method and route to determine FHIR interaction.
    /// </summary>
    private (FhirInteraction interaction, string? resourceType, string? resourceId) ParseRoute(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        var routeValues = ctx.Request.RouteValues;
        var path = ctx.Request.Path.Value ?? string.Empty;

        // Extract from route parameters (works for both direct calls and bundle entries)
        var resourceType = routeValues.TryGetValue("resourceType", out var rt) ? rt as string : null;
        var resourceId = routeValues.TryGetValue("id", out var id) ? id as string : null;

        // Check for compartment search (e.g., /Patient/123/Observation or /Patient/123/*)
        var compartmentType = routeValues.TryGetValue("compartmentType", out var ct) ? ct as string : null;
        var isCompartmentSearch = compartmentType is not null;

        // Check for special endpoints
        var isSearchEndpoint = path.EndsWith("/_search", StringComparison.OrdinalIgnoreCase);
        var isHistoryEndpoint = path.Contains("/_history", StringComparison.OrdinalIgnoreCase);
        var isOperationEndpoint = path.Contains("/$", StringComparison.Ordinal);
        var isMetadataEndpoint = path.EndsWith("/metadata", StringComparison.OrdinalIgnoreCase);

        // Metadata always returns Capabilities
        if (isMetadataEndpoint)
        {
            return (FhirInteraction.Capabilities, null, null);
        }

        // Determine interaction from method + route pattern
        var interaction = (method.ToUpperInvariant(), resourceId != null, isSearchEndpoint, isHistoryEndpoint, isOperationEndpoint, isCompartmentSearch) switch
        {
            // Operation endpoints
            (_, _, _, _, true, _) when resourceId != null => FhirInteraction.OperationInstance,
            (_, _, _, _, true, _) when resourceType != null => FhirInteraction.OperationType,
            (_, _, _, _, true, _) => FhirInteraction.OperationSystem,

            // History endpoints
            ("GET", _, _, true, _, _) when resourceId != null => FhirInteraction.HistoryInstance,
            ("GET", _, _, true, _, _) => FhirInteraction.HistoryType,

            // Compartment search (e.g., /Patient/123/Observation or /Patient/123/*)
            ("GET", _, _, _, _, true) => FhirInteraction.SearchType,

            // Search endpoints
            (_, _, true, _, _, _) => FhirInteraction.SearchType,
            ("GET", false, _, _, _, _) when resourceType != null => FhirInteraction.SearchType,
            ("GET", false, _, _, _, _) when resourceType == null => FhirInteraction.SearchSystem,

            // CRUD operations
            ("GET", true, _, _, _, _) => FhirInteraction.Read,
            ("PUT", _, _, _, _, _) => FhirInteraction.Update,
            ("POST", false, _, _, _, _) when resourceType == null && path.EndsWith("/", StringComparison.Ordinal) => FhirInteraction.Transaction,
            ("POST", false, _, _, _, _) when resourceType != null => FhirInteraction.Create,
            ("DELETE", _, _, _, _, _) => FhirInteraction.Delete,
            ("PATCH", _, _, _, _, _) => FhirInteraction.Patch,

            // Default
            _ => FhirInteraction.Read // Default fallback
        };

        // For compartment search, the "resourceType" for authorization is the compartment type
        // (e.g., Patient compartment search requires Patient read access)
        if (isCompartmentSearch && resourceType is null)
        {
            resourceType = compartmentType;
        }

        return (interaction, resourceType, resourceId);
    }
}
