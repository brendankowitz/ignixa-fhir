// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Services;

/// <summary>
/// Implementation of the FHIR authorization service.
/// Executes handlers in priority order with fail-fast behavior.
/// Merges data filters from multiple handlers.
/// </summary>
public class FhirAuthorizationService : IFhirAuthorizationService
{
    private readonly IEnumerable<IAuthorizationHandler> _handlers;
    private readonly ILogger<FhirAuthorizationService> _logger;

    public FhirAuthorizationService(
        IEnumerable<IAuthorizationHandler> handlers,
        ILogger<FhirAuthorizationService> logger)
    {
        // Order handlers by priority (lower first)
        _handlers = handlers?.OrderBy(h => h.Priority).ToList()
                    ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async ValueTask<AuthorizationResult> AuthorizeAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Starting authorization for {Interaction} on {ResourceType}/{ResourceId} (User: {UserId}, Tenant: {TenantId})",
            context.Interaction,
            context.ResourceType ?? "system",
            context.ResourceId ?? "none",
            context.UserId ?? "anonymous",
            context.TenantId ?? "default");

        FhirAuthorizationFilter? accumulatedFilter = null;

        // Execute handlers in priority order
        foreach (var handler in _handlers)
        {
            var result = await handler.HandleAsync(context, cancellationToken);

            if (!result.Allowed)
            {
                _logger.LogWarning(
                    "Authorization denied by {Handler}: {Reason} (User: {UserId}, Resource: {ResourceType}/{ResourceId})",
                    handler.GetType().Name,
                    result.DenialReason,
                    context.UserId ?? "anonymous",
                    context.ResourceType ?? "system",
                    context.ResourceId ?? "none");

                return result; // Fail-fast
            }

            // Merge filters from handlers
            if (result.Filter != null)
            {
                accumulatedFilter = accumulatedFilter?.Merge(result.Filter) ?? result.Filter;

                _logger.LogDebug(
                    "Authorization filter added by {Handler}: PatientFilter={PatientFilter}",
                    handler.GetType().Name,
                    result.Filter.PatientFilter ?? "none");
            }
        }

        _logger.LogDebug(
            "Authorization granted for {Interaction} on {ResourceType}/{ResourceId}",
            context.Interaction,
            context.ResourceType ?? "system",
            context.ResourceId ?? "none");

        return accumulatedFilter != null
            ? AuthorizationResult.SuccessWithFilter(accumulatedFilter)
            : AuthorizationResult.Success();
    }
}
