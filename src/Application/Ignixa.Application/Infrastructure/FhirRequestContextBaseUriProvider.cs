// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Resolves this server's service base URIs from the ambient FHIR request context, falling back to the
/// configured deployment root when there is no request — reindex, $import, and subscription delivery all
/// index resources outside the HTTP pipeline.
/// </summary>
/// <remarks>
/// Request and background paths run the same <see cref="FhirServiceBaseUriResolver"/> over the same tenant,
/// so a reindex classifies a given absolute self-reference exactly as the request that first stored it did.
/// Without a configured root the fallback yields nothing, and background-indexed rows will store
/// self-references as external while request-indexed rows collapsed them — see
/// <see cref="FhirServiceBaseUriResolver"/> for why <c>Fhir:BaseUri</c> is not optional in practice.
/// </remarks>
public sealed class FhirRequestContextBaseUriProvider(
    IFhirRequestContextAccessor requestContextAccessor,
    FhirServiceBaseUriResolver resolver,
    ITenantConfigurationStore configStore) : IFhirBaseUriProvider
{
    /// <inheritdoc />
    public Uri? GetBaseUri() => GetServiceBaseUris() is [var canonical, ..] ? canonical : null;

    /// <inheritdoc />
    public IReadOnlyList<Uri> GetServiceBaseUris()
    {
        var context = requestContextAccessor.RequestContext;

        if (context?.ServiceBaseUris is { Count: > 0 } fromRequest)
        {
            return fromRequest;
        }

        if (context?.TenantId is { } tenantId and > 0)
        {
            // GetAwaiter().GetResult() is safe here: GetServiceBaseUris() is a synchronous interface
            // member (background indexing calls it with no async context to await from), and the
            // production store (AppSettingsTenantConfigurationStore) returns already-completed
            // ValueTasks backed by a Lazy array, so this never actually blocks.
#pragma warning disable CA2012 // Use ValueTasks correctly - store's ValueTasks are already completed
            var tenant = configStore.GetTenantConfigurationAsync(tenantId).GetAwaiter().GetResult();
            if (tenant is not null)
            {
                var soleTenant = configStore.GetAllTenantsAsync().GetAwaiter().GetResult().Count == 1;
#pragma warning restore CA2012
                return resolver.Resolve(
                    requestOrigin: null,
                    new TenantAddressing(tenantId, tenant.Hostnames, IncludeDeploymentRoot: soleTenant));
            }
        }

        return resolver.Resolve(
            requestOrigin: null,
            context?.TenantId,
            FhirServiceBaseUriForm.TenantScoped);
    }
}
