// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Grpc.Core;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Sidecar.Rbac;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// RBAC authorization handler that delegates to sidecar gRPC service.
/// Fail-fast: Throws RpcException if sidecar is unavailable (returns 503 to client).
/// Priority: 30 (same as local RbacAuthorizationHandler).
/// </summary>
public class SidecarRbacAuthorizationHandler(
    RbacService.RbacServiceClient client,
    ILogger<SidecarRbacAuthorizationHandler> logger) : IAuthorizationHandler
{
    public int Priority => 30;

    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        // Skip if using SMART scopes
        if (context.HasSmartScopes)
        {
            logger.LogDebug("Sidecar RBAC: Skipping - SMART scopes present");
            return AuthorizationResult.Success();
        }

        // Skip if no roles
        if (!context.HasRoles)
        {
            logger.LogDebug("Sidecar RBAC: No roles present - passing to next handler");
            return AuthorizationResult.Success();
        }

        try
        {
            var request = new AccessCheckRequest
            {
                UserId = context.UserId ?? "anonymous",
                TenantId = context.TenantId ?? "default",
                DataAction = BuildDataAction(context),
                ResourceType = context.ResourceType ?? "system",
                ResourceId = context.ResourceId ?? string.Empty,
                CorrelationId = context.HttpContext.TraceIdentifier
            };

            // Add user claims (roles)
            foreach (var role in context.Roles!)
            {
                request.UserClaims.Add($"role:{role}", role);
            }

            var response = await client.CheckAccessAsync(request, cancellationToken: cancellationToken);

            if (!response.Authorized)
            {
                logger.LogWarning(
                    "Sidecar RBAC: Access denied - {Reason}",
                    response.ErrorMessage);

                return AuthorizationResult.InsufficientPermissions(
                    context.ResourceType ?? "system",
                    context.Interaction.ToString());
            }

            logger.LogDebug("Sidecar RBAC: Access granted (DecisionId: {DecisionId})", response.DecisionId);
            return AuthorizationResult.Success();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            logger.LogError(ex, "RBAC sidecar unavailable - failing request");
            throw; // Fail-fast: propagate to filter → 503
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RBAC sidecar error");
            throw; // Fail-fast: propagate to filter → 500
        }
    }

    private static string BuildDataAction(FhirAuthorizationContext context)
    {
        var interaction = context.Interaction.ToString().ToUpperInvariant();
        return $"Microsoft.HealthcareApis/fhir/{interaction}";
    }
}
