// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Smart;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// Authorization handler that checks SMART on FHIR scopes.
/// Applies patient compartment filtering for patient/*.* scopes.
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
        var interaction = context.Interaction.ToSmartPermission();

        _logger.LogDebug(
            "SMART scope check: Checking {ScopeCount} scopes for {ResourceType}.{Interaction}",
            scopes.Count,
            resourceType ?? "system",
            interaction);

        // Find matching scope
        var matchingScope = scopes.FirstOrDefault(scope =>
            scope.MatchesResource(resourceType) &&
            scope.MatchesPermission(interaction));

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
            "SMART scope check: Matched scope {Scope} for {ResourceType}.{Interaction}",
            matchingScope.OriginalScope,
            resourceType ?? "system",
            interaction);

        // Build data filter for patient-scoped requests
        FhirAuthorizationFilter? filter = null;

        if (matchingScope.Type == SmartScopeType.Patient)
        {
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
        }

        return ValueTask.FromResult(filter != null
            ? AuthorizationResult.SuccessWithFilter(filter)
            : AuthorizationResult.Success());
    }
}
