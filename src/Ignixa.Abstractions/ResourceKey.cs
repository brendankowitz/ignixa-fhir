namespace Ignixa.Abstractions;

/// <summary>
/// Identifies a FHIR resource by type, ID, optional version, and optional tenant.
/// </summary>
public record ResourceKey(
    string ResourceType,
    string Id,
    string? VersionId = null,
    int? TenantId = null)
{

    /// <summary>
    /// Returns a string representation suitable for logging.
    /// </summary>
    public override string ToString()
    {
        if (TenantId != null)
        {
            // Multi-tenant format: {tenantId}/{resourceType}/{id}[/_history/{versionId}]
            return VersionId == null
                ? $"{TenantId}/{ResourceType}/{Id}"
                : $"{TenantId}/{ResourceType}/{Id}/_history/{VersionId}";
        }

        // Single-tenant format: {resourceType}/{id}[/_history/{versionId}]
        return VersionId == null
            ? $"{ResourceType}/{Id}"
            : $"{ResourceType}/{Id}/_history/{VersionId}";
    }
}
