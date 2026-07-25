// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// The addressing facts <see cref="FhirServiceBaseUriResolver"/> needs to build a tenant's recognition set,
/// independent of how the tenant was resolved (host header, path, or auto-detect).
/// </summary>
/// <param name="TenantId">Internal partition identity.</param>
/// <param name="Hostnames">Hostnames the tenant answers on; the first is canonical. May be empty.</param>
/// <param name="IncludeDeploymentRoot">
/// Whether the bare deployment root is a recognized base for this tenant. Callers must set this true only for
/// the sole tenant; setting it for one of several tenants would conflate <c>host/Patient/1</c> across tenants.
/// Prefer <see cref="For"/>, which derives this correctly from the active tenant count instead of leaving it
/// to each call site.
/// </param>
public sealed record TenantAddressing(
    int TenantId,
    IReadOnlyList<string> Hostnames,
    bool IncludeDeploymentRoot)
{
    /// <summary>
    /// Builds the addressing facts for <paramref name="tenant"/>, deriving <see cref="IncludeDeploymentRoot"/>
    /// from <paramref name="activeTenantCount"/> so the "root only for the sole tenant" rule lives in one
    /// enforceable place instead of being re-derived at every call site.
    /// </summary>
    /// <param name="tenant">The resolved tenant configuration.</param>
    /// <param name="activeTenantCount">
    /// Count of active tenants in the deployment, i.e. <c>GetAllTenantsAsync().Count</c>.
    /// </param>
    public static TenantAddressing For(TenantConfiguration tenant, int activeTenantCount)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (tenant.TenantId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tenant),
                tenant.TenantId,
                "Tenant addressing requires a positive tenant id; tenant 0 is the reserved system partition.");
        }

        return new TenantAddressing(tenant.TenantId, tenant.Hostnames ?? [], IncludeDeploymentRoot: activeTenantCount == 1);
    }
}
