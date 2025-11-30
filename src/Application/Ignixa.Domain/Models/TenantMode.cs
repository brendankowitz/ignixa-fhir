namespace Ignixa.Domain.Models;

/// <summary>
/// Tenant mode: Isolated (single repository per tenant) or Distributed (fanout/union queries).
/// This is a system-wide setting that applies to all tenants in the instance.
/// </summary>
public enum TenantMode
{
    /// <summary>
    /// Isolated mode: Each tenant has its own isolated data store.
    /// No cross-tenant queries. This is the default and currently supported mode.
    /// </summary>
    Isolated,

    /// <summary>
    /// Distributed mode: Fanout/union queries across multiple data layers.
    /// Enables cross-tenant research queries with proper authorization.
    /// Not yet supported - reserved for future implementation (Phase 20.2+).
    /// </summary>
    Distributed
}
