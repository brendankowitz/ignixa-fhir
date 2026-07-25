// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Which of a tenant's equivalent service bases is the canonical one to emit.
/// </summary>
public enum FhirServiceBaseUriForm
{
    /// <summary>
    /// The deployment root, e.g. <c>https://host/</c> — what a tenant-agnostic route hands out.
    /// </summary>
    Root,

    /// <summary>
    /// The tenant-scoped base, e.g. <c>https://host/tenant/1/</c> — what a <c>/tenant/{id}/</c> route
    /// hands out.
    /// </summary>
    TenantScoped
}
