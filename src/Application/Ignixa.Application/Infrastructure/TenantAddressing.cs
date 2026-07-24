// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// The addressing facts <see cref="FhirServiceBaseUriResolver"/> needs to build a tenant's recognition set,
/// independent of how the tenant was resolved (host header, path, or auto-detect).
/// </summary>
/// <param name="TenantId">Internal partition identity.</param>
/// <param name="Hostnames">Hostnames the tenant answers on; the first is canonical. May be empty.</param>
/// <param name="IncludeDeploymentRoot">
/// Whether the bare deployment root is a recognized base for this tenant. True only for the sole tenant of a
/// single-tenant deployment, where <c>example.org/Patient</c> is a valid self-reference. Including it for one
/// of several tenants would conflate <c>example.org/Patient/1</c> across tenants.
/// </param>
public sealed record TenantAddressing(
    int TenantId,
    IReadOnlyList<string> Hostnames,
    bool IncludeDeploymentRoot);
