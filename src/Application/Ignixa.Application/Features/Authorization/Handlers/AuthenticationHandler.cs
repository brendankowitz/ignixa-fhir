// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// Authorization handler that checks if the request is authenticated.
/// Always allows access to /metadata (CapabilityStatement) endpoint.
/// Priority: 10 (first handler in pipeline).
/// </summary>
public class AuthenticationHandler : IAuthorizationHandler
{
    private readonly ILogger<AuthenticationHandler> _logger;

    public AuthenticationHandler(ILogger<AuthenticationHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Priority => 10;

    /// <inheritdoc />
    public ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        // Always allow access to /metadata (CapabilityStatement)
        if (context.Interaction == FhirInteraction.Capabilities)
        {
            _logger.LogDebug("Authentication check: Allowing unauthenticated access to /metadata");
            return ValueTask.FromResult(AuthorizationResult.Success());
        }

        // Require authentication for all other endpoints
        if (!context.IsAuthenticated)
        {
            _logger.LogWarning("Authentication check: Request denied - not authenticated");
            return ValueTask.FromResult(AuthorizationResult.AuthenticationRequired);
        }

        _logger.LogDebug(
            "Authentication check: User {UserId} authenticated",
            context.UserId ?? context.SmartContext?.TokenClaims.FhirUser ?? "unknown");

        return ValueTask.FromResult(AuthorizationResult.Success());
    }
}
