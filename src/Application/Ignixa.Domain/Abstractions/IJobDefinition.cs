// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Marker interface for background job definitions that require tenant isolation.
/// All job definition types must implement this interface to work with IBackgroundJobRepository.
/// This interface enables compile-time tenant validation without reflection.
/// </summary>
public interface IJobDefinition
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation (stored in definition payload, not schema).
    /// </summary>
    int TenantId { get; }
}
