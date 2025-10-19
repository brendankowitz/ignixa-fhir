// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Domain;

/// <summary>
/// Represents the tenant context for a request.
/// Phase 1: Single-tenant mode (TenantId is always null).
/// Phase 2+: Multi-tenant mode with custom search parameters and structure definitions per tenant.
/// </summary>
public sealed class TenantContext
{
    /// <summary>
    /// Gets the singleton instance representing the default (single-tenant) context.
    /// </summary>
    public static TenantContext Default { get; } = new TenantContext(null);

    /// <summary>
    /// Gets the tenant identifier, or null for single-tenant mode.
    /// </summary>
    public string? TenantId { get; }

    private TenantContext(string? tenantId)
    {
        TenantId = tenantId;
    }

    /// <summary>
    /// Creates a tenant context for the specified tenant ID.
    /// </summary>
    public static TenantContext Create(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Default;
        }

        return new TenantContext(tenantId);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is TenantContext other && TenantId == other.TenantId;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return TenantId?.GetHashCode(StringComparison.Ordinal) ?? 0;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return TenantId ?? "(default)";
    }
}
