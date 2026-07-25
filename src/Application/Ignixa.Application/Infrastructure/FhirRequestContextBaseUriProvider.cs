// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

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
/// <param name="logger">
/// Optional so existing call sites that construct this type directly (it is registered via a factory lambda,
/// not reflection) keep compiling; wire a real <see cref="ILogger{TCategoryName}"/> through DI where possible
/// so the inactive-tenant fallback below is observable.
/// </param>
public sealed class FhirRequestContextBaseUriProvider(
    IFhirRequestContextAccessor requestContextAccessor,
    FhirServiceBaseUriResolver resolver,
    ITenantConfigurationStore configStore,
    ILogger<FhirRequestContextBaseUriProvider>? logger = null) : IFhirBaseUriProvider
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
            var allTenantCount = tenant is not null
                ? configStore.GetAllTenantsAsync().GetAwaiter().GetResult().Count
                : 0;
#pragma warning restore CA2012

            if (tenant is not null)
            {
                return resolver.Resolve(requestOrigin: null, TenantAddressing.For(tenant, allTenantCount));
            }

            // The store gates GetTenantConfigurationAsync on IsActive, so this branch is reached specifically
            // when the tenant was deactivated after being addressed. The numeric-form fallback below still
            // returns a usable base, but it is not necessarily the base an active request for this tenant
            // would have used (a configured hostname or the deployment root), so background indexing can
            // silently drift from the request path until this is surfaced.
            logger?.LogWarning(
                "Tenant {TenantId} has no active configuration; background service base URI resolution fell " +
                "back to the numeric tenant-scoped path form, which may not match the canonical base an " +
                "active request for this tenant would use.",
                tenantId);
        }

        return resolver.Resolve(
            requestOrigin: null,
            context?.TenantId,
            FhirServiceBaseUriForm.TenantScoped);
    }
}
