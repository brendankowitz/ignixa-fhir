// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Smart;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// Authorization handler that checks SMART on FHIR v2 scopes.
/// Applies patient/practitioner compartment filtering for context-scoped requests.
/// Priority: 40 (runs after RBAC).
/// </summary>
public class SmartScopeAuthorizationHandler : IAuthorizationHandler
{
    private readonly ILogger<SmartScopeAuthorizationHandler> _logger;

    public SmartScopeAuthorizationHandler(ILogger<SmartScopeAuthorizationHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Priority => 40;

    /// <inheritdoc />
    public ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        // Skip if not SMART authenticated
        if (context.SmartContext == null)
        {
            _logger.LogDebug("SMART scope check: Skipping - no SMART context");
            return ValueTask.FromResult(AuthorizationResult.Success());
        }

        var scopes = context.SmartContext.Scopes;
        var resourceType = context.ResourceType;
        var interaction = context.Interaction.ToFhirCode();

        _logger.LogDebug(
            "SMART scope check: Checking {ScopeCount} scopes for {ResourceType}.{Interaction}",
            scopes.Count,
            resourceType ?? "system",
            interaction);

        // Find matching scope using SMART v2 matching
        var matchingScope = scopes.FirstOrDefault(scope =>
            scope.MatchesResource(resourceType) &&
            scope.MatchesInteraction(interaction));

        if (matchingScope == null)
        {
            _logger.LogWarning(
                "SMART scope check: Request denied - no scope grants {Interaction} access to {ResourceType}",
                interaction,
                resourceType ?? "system");

            return ValueTask.FromResult(AuthorizationResult.InsufficientPermissions(
                resourceType ?? "system",
                interaction));
        }

        _logger.LogDebug(
            "SMART scope check: Matched scope {Scope} (permissions: {Permissions}) for {ResourceType}.{Interaction}",
            matchingScope.OriginalScope,
            matchingScope.PermissionString,
            resourceType ?? "system",
            interaction);

        // Build data filter for context-scoped requests
        FhirAuthorizationFilter? filter = null;

        switch (matchingScope.Type)
        {
            case SmartScopeType.Patient:
                var patientId = context.SmartContext.PatientContext;
                if (string.IsNullOrEmpty(patientId))
                {
                    _logger.LogWarning(
                        "SMART scope check: Request denied - patient scope {Scope} requires patient context",
                        matchingScope.OriginalScope);

                    return ValueTask.FromResult(AuthorizationResult.Denied(
                        "Patient scope requires patient context"));
                }

                _logger.LogDebug(
                    "SMART scope check: Applying patient compartment filter for patient {PatientId}",
                    patientId);

                filter = FhirAuthorizationFilter.ForPatient(patientId);

                // Apply search constraints if present (SMART v2 feature)
                if (matchingScope.SearchConstraints != null && matchingScope.SearchConstraints.Count > 0)
                {
                    // Security: POST _search with constrained scopes is not supported
                    // because we don't parse POST body parameters for constraint validation
                    if (context.Interaction == FhirInteraction.SearchType &&
                        context.HttpContext.Request.Method == "POST")
                    {
                        _logger.LogWarning(
                            "SMART scope check: POST _search with search constraints is not supported. " +
                            "Scope: {Scope}, User: {UserId}",
                            matchingScope.OriginalScope,
                            context.UserId);

                        return ValueTask.FromResult(AuthorizationResult.Denied(
                            "POST _search is not supported with constrained SMART scopes. " +
                            "Please use GET with query parameters instead."));
                    }

                    filter = filter with
                    {
                        SearchFilters = new Dictionary<string, string>(matchingScope.SearchConstraints)
                    };
                }
                break;

            case SmartScopeType.Practitioner:
                var practitionerId = context.SmartContext.UserContext;
                if (string.IsNullOrEmpty(practitionerId))
                {
                    _logger.LogWarning(
                        "SMART scope check: Request denied - practitioner scope {Scope} requires practitioner context",
                        matchingScope.OriginalScope);

                    return ValueTask.FromResult(AuthorizationResult.Denied(
                        "Practitioner scope requires practitioner context (fhirUser claim)"));
                }

                _logger.LogDebug(
                    "SMART scope check: Applying practitioner compartment filter for {PractitionerId}",
                    practitionerId);

                filter = FhirAuthorizationFilter.ForPractitioner(practitionerId);
                break;

            case SmartScopeType.User:
            case SmartScopeType.System:
                // User and system scopes don't require compartment filtering
                // but may have search constraints
                if (matchingScope.SearchConstraints != null && matchingScope.SearchConstraints.Count > 0)
                {
                    // Security: POST _search with constrained scopes is not supported
                    // because we don't parse POST body parameters for constraint validation
                    if (context.Interaction == FhirInteraction.SearchType &&
                        context.HttpContext.Request.Method == "POST")
                    {
                        _logger.LogWarning(
                            "SMART scope check: POST _search with search constraints is not supported. " +
                            "Scope: {Scope}, User: {UserId}",
                            matchingScope.OriginalScope,
                            context.UserId);

                        return ValueTask.FromResult(AuthorizationResult.Denied(
                            "POST _search is not supported with constrained SMART scopes. " +
                            "Please use GET with query parameters instead."));
                    }

                    filter = new FhirAuthorizationFilter
                    {
                        SearchFilters = new Dictionary<string, string>(matchingScope.SearchConstraints)
                    };
                }
                break;
        }

        return ValueTask.FromResult(filter != null
            ? AuthorizationResult.SuccessWithFilter(filter)
            : AuthorizationResult.Success());
    }
}
