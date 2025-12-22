// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Models;

/// <summary>
/// Input for the TTL cleanup activity (per-tenant).
/// </summary>
/// <param name="TenantId">The tenant to clean up expired resources for.</param>
/// <param name="BatchSize">Maximum number of expired resources to process.</param>
public record TtlCleanupActivityInput(int TenantId, int BatchSize);
