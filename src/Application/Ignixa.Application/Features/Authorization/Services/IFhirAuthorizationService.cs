// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;

namespace Ignixa.Application.Features.Authorization.Services;

/// <summary>
/// Main authorization service interface.
/// Coordinates all authorization handlers in the pipeline.
/// </summary>
public interface IFhirAuthorizationService
{
    /// <summary>
    /// Authorizes a FHIR interaction request.
    /// Runs all handlers in priority order (fail-fast on denial).
    /// </summary>
    /// <param name="context">The authorization context containing request details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authorization result with decision and any data filters.</returns>
    ValueTask<AuthorizationResult> AuthorizeAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken = default);
}
