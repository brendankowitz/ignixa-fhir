namespace Ignixa.Domain.Models;

/// <summary>
/// Configuration for a tenant, including FHIR version and storage settings.
/// Note: Partition 0 is reserved for system operations (see SystemConstants.SystemPartitionId).
/// </summary>
public record TenantConfiguration
{
    /// <summary>
    /// Unique tenant identifier (0, 1, 2, ...)
    /// NOTE: TenantId 0 is reserved for system operations and should have IsSystemPartition = true.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Display name for the tenant (e.g., "Mayo Clinic")
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// FHIR version for this tenant (e.g., "4.0" for R4, "5.0" for R5)
    /// </summary>
    public required string FhirVersion { get; init; }

    /// <summary>
    /// Whether this tenant is active and accepting requests
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Whether this partition is reserved for system operations.
    /// System partitions (typically Partition 0):
    /// - Are used for transaction ID allocation and system metadata
    /// - Cannot be accessed via /tenant/{id}/ API routes
    /// - Are filtered out from GetAllTenantsAsync() results
    /// - Can only be accessed internally by system components
    /// </summary>
    public bool IsSystemPartition { get; init; }

    /// <summary>
    /// Storage configuration for this tenant
    /// </summary>
    public TenantStorageConfiguration Storage { get; init; } = new();

    /// <summary>
    /// Search configuration for this tenant
    /// </summary>
    public TenantSearchConfiguration Search { get; init; } = new();

    /// <summary>
    /// Validation tier for this tenant (None, Fast, Spec, Profile).
    /// Defaults to Spec (recommended for production).
    /// Fast = Universal checks only (less than 25ms).
    /// Spec = Fast + Schema checks (less than 200ms).
    /// Profile = Spec + Advanced profile validation (less than 1000ms).
    /// </summary>
    public string ValidationTier { get; init; } = "Spec";
}

/// <summary>
/// Storage configuration for a tenant.
/// </summary>
public record TenantStorageConfiguration
{
    /// <summary>
    /// Storage type: "FileSystem", "SqlServer", "CosmosDb"
    /// </summary>
    public string Type { get; init; } = "FileSystem";

    /// <summary>
    /// Base directory for FileSystem storage (relative to global base directory).
    /// Example: "tenants/0"
    /// </summary>
    public string? BaseDirectory { get; init; }

    /// <summary>
    /// Connection string for database storage (SqlServer, CosmosDb).
    /// </summary>
    public string? ConnectionString { get; init; }
}

/// <summary>
/// Search configuration for a tenant.
/// </summary>
public record TenantSearchConfiguration
{
    /// <summary>
    /// Search type: "InMemory", "Sql", "Elastic"
    /// </summary>
    public string Type { get; init; } = "InMemory";

    /// <summary>
    /// Connection string for external search engines (Elastic, etc.).
    /// </summary>
    public string? ConnectionString { get; init; }
}
