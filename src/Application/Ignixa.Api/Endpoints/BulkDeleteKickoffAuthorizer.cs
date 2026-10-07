// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Infrastructure;
using Ignixa.Search.Parsing;
using Microsoft.Extensions.Options;
using AuthorizationEndpointFilter = Ignixa.Api.Filters.FhirAuthorizationFilter;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Authorizes a <c>$bulk-delete</c> kickoff for the deletes and updates the job will actually perform.
/// </summary>
/// <remarks>
/// <para>
/// The route-level <see cref="AuthorizationEndpointFilter"/> classifies a kickoff as <c>operation-type</c>/
/// <c>operation-system</c>, which SMART maps to no permission at all (any scope on the type passes) and
/// RBAC checks only against the route's type. That is right for read-only operations and wrong for a
/// destructive one, so this step re-authorizes the request, through the same handler pipeline, as
/// <see cref="FhirInteraction.Delete"/> on every type the job can delete -- and as
/// <see cref="FhirInteraction.Update"/> on every type when <c>_remove-references</c> rewrites referrers.
/// </para>
/// <para>
/// The type set is derived from the request rather than the job's snapshot, and errs wide: the route's
/// type, or each <c>_type</c> entry, or every type (the <c>*</c> grant) when the request has no
/// <c>_type</c> or cascades through <c>_include</c>/<c>_revinclude</c>, which reach resources of types
/// the request never names. <c>excludedResourceTypes</c> only narrows the job, so it is ignored here.
/// </para>
/// <para>
/// A grant that carries a compartment or search constraint is refused: the job deletes everything that
/// matches the request, not just what the caller may see.
/// </para>
/// </remarks>
public sealed class BulkDeleteKickoffAuthorizer(
    IFhirAuthorizationService authorizationService,
    IFhirRequestContextAccessor fhirContextAccessor,
    IOptions<AuthorizationOptions> authorizationOptions,
    ILogger<BulkDeleteKickoffAuthorizer> logger)
{
    /// <summary>
    /// Authorizes the kickoff. Returns a denied result (to be reported as 403) at the first requirement
    /// the caller is not granted, or that is granted only under a data restriction.
    /// </summary>
    /// <param name="httpContext">The kickoff request, whose user claims are authorized.</param>
    /// <param name="routeResourceType">The route's resource type for a type-level kickoff; null at system level.</param>
    /// <param name="request">The parsed kickoff request.</param>
    /// <param name="cancellationToken">Cancels the authorization calls.</param>
    public async ValueTask<AuthorizationResult> AuthorizeAsync(
        HttpContext httpContext,
        string? routeResourceType,
        BulkDeleteRequestParseResult request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);

        if (!authorizationOptions.Value.Enabled)
        {
            return AuthorizationResult.Success();
        }

        var fhirContext = fhirContextAccessor.RequestContext
            ?? throw new InvalidOperationException(
                "FhirRequestContext not set. Ensure TenantResolutionMiddleware runs before bulk delete authorization.");

        foreach (var requirement in GetRequirements(routeResourceType, request))
        {
            var context = AuthorizationEndpointFilter.CreateAuthorizationContext(
                httpContext, fhirContext, requirement.Interaction, requirement.ResourceType, resourceId: null);
            var result = await authorizationService.AuthorizeAsync(context, cancellationToken);

            var denial = GetDenial(requirement, result);
            if (denial is not null)
            {
                logger.LogWarning(
                    "Bulk delete kickoff denied for user {User} on tenant {TenantId}: {Denial}",
                    context.UserId ?? "anonymous",
                    fhirContext.TenantId,
                    denial);
                return AuthorizationResult.Denied(denial);
            }
        }

        return AuthorizationResult.Success();
    }

    /// <summary>
    /// The access a kickoff needs, in the order it is checked.
    /// </summary>
    internal static IReadOnlyList<BulkDeleteAccessRequirement> GetRequirements(
        string? routeResourceType,
        BulkDeleteRequestParseResult request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = request.SearchParameters
            .Select(parameter => new QueryParameter(parameter.Key, parameter.Value))
            .ToList();
        var requirements = new List<BulkDeleteAccessRequirement>();

        if (parameters.Any(parameter => parameter.Category is ParameterCategory.Include or ParameterCategory.RevInclude))
        {
            requirements.Add(new(
                FhirInteraction.Delete, null, "_include/_revinclude cascade the deletion to resources of any type"));
        }
        else if (routeResourceType is not null)
        {
            requirements.Add(new(FhirInteraction.Delete, routeResourceType, $"the job deletes {routeResourceType} resources"));
        }
        else
        {
            var requestedTypes = parameters
                .Where(parameter => parameter.Category == ParameterCategory.Type)
                .SelectMany(parameter => parameter.Value.Split(
                    ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            if (requestedTypes.Count == 0)
            {
                requirements.Add(new(
                    FhirInteraction.Delete, null, "a system-level bulk delete without _type deletes every resource type"));
            }
            else
            {
                requirements.AddRange(requestedTypes.Select(type =>
                    new BulkDeleteAccessRequirement(FhirInteraction.Delete, type, $"_type names {type}")));
            }
        }

        if (request.RemoveReferences)
        {
            requirements.Add(new(
                FhirInteraction.Update, null, "_remove-references rewrites referencing resources of any type"));
        }

        return requirements;
    }

    private static string? GetDenial(BulkDeleteAccessRequirement requirement, AuthorizationResult result)
    {
        if (!result.Allowed)
        {
            return $"Bulk delete requires {Describe(requirement)} because {requirement.Reason}. {result.DenialReason}";
        }

        return result.Filter is { RestrictsData: true }
            ? $"Bulk delete requires unrestricted {Describe(requirement)} because {requirement.Reason}; " +
              "the caller's grant is limited to a compartment or search constraint."
            : null;
    }

    private static string Describe(BulkDeleteAccessRequirement requirement) =>
        $"{requirement.Interaction.ToFhirCode()} access to {requirement.ResourceType ?? "all resource types (*)"}";
}
