// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// Interface for authorization handlers in the FHIR authorization pipeline.
/// Handlers are executed in priority order (lower priority = earlier execution).
/// </summary>
public interface IAuthorizationHandler
{
    /// <summary>
    /// Priority of this handler in the authorization pipeline.
    /// Lower values execute first. Recommended values:
    /// - 10: Authentication check
    /// - 20: Tenant isolation
    /// - 30: RBAC (role-based)
    /// - 40: SMART scopes
    /// - 50: Capability enforcement
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Handles an authorization check for the given context.
    /// </summary>
    /// <param name="context">The authorization context containing request details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authorization result indicating allowed/denied and any data filters.</returns>
    ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken);
}
