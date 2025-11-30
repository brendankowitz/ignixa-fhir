namespace Ignixa.Domain.Models;

/// <summary>
/// Package configuration for a tenant.
/// </summary>
public record TenantPackageConfiguration
{
    /// <summary>
    /// List of packages to preload at startup (format: "packageId@version").
    /// Examples: "hl7.fhir.us.core@5.0.1", "hl7.fhir.sql@0.1.0"
    /// </summary>
    public IReadOnlyList<string> PreloadPackages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether to automatically load packages during tenant initialization.
    /// </summary>
    public bool EnableAutoLoad { get; init; }
}

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
    /// Validation depth for this tenant (Minimal, Spec, Full).
    /// Defaults to Spec (recommended for production).
    /// Minimal = Universal checks only (less than 25ms).
    /// Spec = Minimal + Schema checks + required terminology (less than 200ms).
    /// Full = Spec + Advanced profile validation + extensible terminology (less than 1000ms).
    /// </summary>
    public string ValidationDepth { get; init; } = "Spec";

    /// <summary>
    /// Package preload configuration for this tenant.
    /// </summary>
    public TenantPackageConfiguration Packages { get; init; } = new();
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
    /// If null/empty for system partition (partition 0), the connection string will be inherited
    /// from the tenant specified by InheritConnectionStringFromTenant (default: tenant 1).
    /// This allows single-tenant deployments to avoid extra database infrastructure.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// For system partition (partition 0) with null ConnectionString:
    /// Specifies which tenant's connection string to inherit (default: 1).
    /// Only used when ConnectionString is null/empty and this is a system partition.
    /// </summary>
    public int InheritConnectionStringFromTenant { get; init; } = 1;
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
