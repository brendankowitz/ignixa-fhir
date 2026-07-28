using System.Globalization;
using System.Text;
using System.Text.Json;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Models;
using Ignixa.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.PackageManagement;

/// <summary>
/// <see cref="IPackageResourceRepository"/> over <see cref="ISqlExecutionService"/>, replacing the EF
/// implementation. <c>PackageResourceEntity</c> matched <c>dbo.PackageResource</c> exactly, so this is a
/// straight port and the EF version was usable as an oracle for every assertion.
/// <para>
/// <b>The <c>fhirVersion</c> parameter is accepted and not used, on seven methods.</b> That is inherited,
/// not new. Every caller-facing version string is <c>"R4"</c>/<c>"R4B"</c>/<c>"R5"</c>/<c>"Stu3"</c>
/// (see <c>OperationsSegment.GetFhirVersionString</c>), while <c>PackageResource.FhirVersion</c> holds what
/// the NPM package manifest declared — <c>"4.0.1"</c>, <c>"4.3.0"</c>, <c>"5.0.0"</c>. A direct equality
/// filter therefore matches nothing, and switching it on would silently empty the CapabilityStatement's
/// operations and the StructureDefinition summaries. Honouring it needs a normalisation layer plus
/// set-membership semantics, because manifests declare a *list* of supported versions. Each affected method
/// says so at its own filter site, and <c>SqlServerPackageResourceRepositoryVersionFilterTests</c> pins the
/// current behaviour so those tests fail deliberately the day normalisation lands.
/// </para>
/// </summary>
public sealed class SqlServerPackageResourceRepository(
    ISqlExecutionService sqlExecutionService,
    int connectionTenantId,
    ILogger<SqlServerPackageResourceRepository> logger) : IPackageResourceRepository
{
    private static readonly TableDescriptor Packages = SqlCatalog.Default.Table("PackageResource");

    private static readonly string SelectColumns = string.Join(", ", new[]
    {
        "PackageResourceId", "PackageId", "PackageVersion", "ResourceType", "Canonical", "Version",
        "ResourceId", "ResourceJson", "FhirVersion", "LoadedDate", "IsActive",
    }.Select(c => Packages.Column(c).Name));

    private static readonly string QualifiedTable = $"{Packages.SchemaName}.{Packages.TableName}";

    // Every SQL string here is assembled from catalog identifiers and fixed literals; all caller data flows
    // through parameters. Stating the CA2100 justification once rather than at each call site.
    private static SqlCommand Command(string sql)
    {
#pragma warning disable CA2100
        return new SqlCommand(sql);
#pragma warning restore CA2100
    }

    public async Task UpsertAsync(PackageResource packageResource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        try
        {
            await UpsertOneAsync(packageResource, cancellationToken);
        }
        catch (SqlException ex) when (IsDuplicateKey(ex))
        {
            // Concurrent package loads are expected (TenantPackagePreloadService and
            // EmbeddedPackagePreloadService can run together), and the other writer's row is the same row.
            // Treated as idempotent success. Detected by error number rather than by substring-matching the
            // message, which is what the EF version did and what SqlSystemRepository was already corrected
            // away from -- a message match claims unrelated duplicate-key failures as this resource's race.
            logger.LogWarning(
                ex,
                "Package resource {Canonical} from package {PackageId}@{PackageVersion} encountered duplicate key. " +
                "Another thread is loading this package; treating as already written.",
                packageResource.Canonical, packageResource.PackageId, packageResource.PackageVersion);
        }
    }

    public async Task BatchUpsertAsync(IReadOnlyList<PackageResource> packageResources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResources);

        if (packageResources.Count == 0)
        {
            return;
        }

        var packageId = packageResources[0].PackageId;
        var packageVersion = packageResources[0].PackageVersion;

        logger.LogInformation(
            "Batch upserting {Count} resources from package {PackageId}@{PackageVersion}",
            packageResources.Count, packageId, packageVersion);

        try
        {
            foreach (var resource in packageResources)
            {
                await UpsertOneAsync(resource, cancellationToken);
            }

            logger.LogInformation(
                "Successfully upserted {Count} resources from package {PackageId}@{PackageVersion}",
                packageResources.Count, packageId, packageVersion);
        }
        catch (SqlException ex) when (IsDuplicateKey(ex))
        {
            logger.LogWarning(
                ex,
                "Package {PackageId}@{PackageVersion}: batch upsert encountered duplicate key constraint. " +
                "Another thread is loading the same package; treating as already written.",
                packageId, packageVersion);
        }
    }

    /// <summary>
    /// Insert-or-update against the identity the unique index defines: PackageId + PackageVersion +
    /// ResourceType + ResourceId. The UPDATE arm deliberately leaves Canonical, PackageId and PackageVersion
    /// alone (they are the identity) and the six terminology-import columns alone, so re-loading a package
    /// does not discard import progress already recorded against its resources.
    /// </summary>
    private async Task UpsertOneAsync(PackageResource resource, CancellationToken cancellationToken)
    {
        using var command = Command(
            $"UPDATE {QualifiedTable} SET " +
            $"{Packages.Column("Version").Name} = @version, " +
            $"{Packages.Column("ResourceJson").Name} = @resourceJson, " +
            $"{Packages.Column("FhirVersion").Name} = @fhirVersion, " +
            $"{Packages.Column("LoadedDate").Name} = @loadedDate, " +
            $"{Packages.Column("IsActive").Name} = @isActive " +
            $"WHERE {Packages.Column("PackageId").Name} = @packageId " +
            $"AND {Packages.Column("PackageVersion").Name} = @packageVersion " +
            $"AND {Packages.Column("ResourceType").Name} = @resourceType " +
            $"AND {Packages.Column("ResourceId").Name} = @resourceId; " +
            "IF @@ROWCOUNT = 0 " +
            $"INSERT INTO {QualifiedTable} ({InsertColumns}) VALUES " +
            "(@packageId, @packageVersion, @resourceType, @canonical, @version, @resourceId, " +
            "@resourceJson, @fhirVersion, @loadedDate, @isActive);");

        command.Parameters.AddWithValue("@packageId", resource.PackageId);
        command.Parameters.AddWithValue("@packageVersion", resource.PackageVersion);
        command.Parameters.AddWithValue("@resourceType", resource.ResourceType);
        command.Parameters.AddWithValue("@canonical", resource.Canonical);
        command.Parameters.AddWithValue("@version", (object?)resource.Version ?? DBNull.Value);
        command.Parameters.AddWithValue("@resourceId", resource.ResourceId);
        command.Parameters.AddWithValue("@resourceJson", resource.ResourceJson);
        command.Parameters.AddWithValue("@fhirVersion", resource.FhirVersion);
        command.Parameters.AddWithValue("@loadedDate", resource.LoadedDate);
        command.Parameters.AddWithValue("@isActive", resource.IsActive);

        await sqlExecutionService.ExecuteNonQueryAsync(connectionTenantId, command, cancellationToken);
    }

    private static readonly string InsertColumns = string.Join(", ", new[]
    {
        "PackageId", "PackageVersion", "ResourceType", "Canonical", "Version", "ResourceId",
        "ResourceJson", "FhirVersion", "LoadedDate", "IsActive",
    }.Select(c => Packages.Column(c).Name));

    public async Task<PackageResource?> GetByCanonicalAsync(
        string canonical, string? version = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        var versionFilter = string.IsNullOrEmpty(version)
            ? string.Empty
            : $" AND {Packages.Column("Version").Name} = @version";

        using var command = Command(
            $"SELECT TOP 1 {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("Canonical").Name} = @canonical AND {ActiveOnly}{versionFilter}");
        command.Parameters.AddWithValue("@canonical", canonical);
        if (!string.IsNullOrEmpty(version))
        {
            command.Parameters.AddWithValue("@version", version);
        }

        return await SingleAsync(command, cancellationToken);
    }

    public async Task<PackageResource?> GetFromPackageAsync(
        string packageId, string packageVersion, string canonical, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        using var command = Command(
            $"SELECT TOP 1 {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("PackageId").Name} = @packageId " +
            $"AND {Packages.Column("PackageVersion").Name} = @packageVersion " +
            $"AND {Packages.Column("Canonical").Name} = @canonical AND {ActiveOnly}");
        command.Parameters.AddWithValue("@packageId", packageId);
        command.Parameters.AddWithValue("@packageVersion", packageVersion);
        command.Parameters.AddWithValue("@canonical", canonical);

        return await SingleAsync(command, cancellationToken);
    }

    public async Task<PackageResource?> GetLatestByCanonicalAsync(
        string canonical, string? resourceType = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        var typeFilter = string.IsNullOrEmpty(resourceType)
            ? string.Empty
            : $" AND {Packages.Column("ResourceType").Name} = @resourceType";

        using var command = Command(
            $"SELECT {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("Canonical").Name} = @canonical AND {ActiveOnly}{typeFilter}");
        command.Parameters.AddWithValue("@canonical", canonical);
        if (!string.IsNullOrEmpty(resourceType))
        {
            command.Parameters.AddWithValue("@resourceType", resourceType);
        }

        var candidates = await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadResource, cancellationToken);

        // Ordered by semantic version, not lexically. The EF implementation ordered by the raw string while
        // carrying a comment claiming PARSENAME-based semver parsing that was never written, so "1.10.0"
        // sorted below "1.9.0" and the wrong row was returned as "latest".
        return candidates
            .OrderByDescending(r => r.PackageVersion, SemanticVersionComparer.Instance)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<PackageResource>> ListPackageResourcesAsync(
        string packageId, string packageVersion, string? resourceType = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        var typeFilter = string.IsNullOrEmpty(resourceType)
            ? string.Empty
            : $" AND {Packages.Column("ResourceType").Name} = @resourceType";

        using var command = Command(
            $"SELECT {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("PackageId").Name} = @packageId " +
            $"AND {Packages.Column("PackageVersion").Name} = @packageVersion AND {ActiveOnly}{typeFilter} " +
            $"ORDER BY {Packages.Column("ResourceType").Name}, {Packages.Column("Canonical").Name}");
        command.Parameters.AddWithValue("@packageId", packageId);
        command.Parameters.AddWithValue("@packageVersion", packageVersion);
        if (!string.IsNullOrEmpty(resourceType))
        {
            command.Parameters.AddWithValue("@resourceType", resourceType);
        }

        return await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadResource, cancellationToken);
    }

    public async Task<IReadOnlyList<(string PackageId, string PackageVersion)>> ListLoadedPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        using var command = Command(
            $"SELECT DISTINCT {Packages.Column("PackageId").Name}, {Packages.Column("PackageVersion").Name} " +
            $"FROM {QualifiedTable} WHERE {ActiveOnly} " +
            $"ORDER BY {Packages.Column("PackageId").Name}, {Packages.Column("PackageVersion").Name}");

        return await sqlExecutionService.ExecuteReaderAsync(
            connectionTenantId, command, reader => (reader.GetString(0), reader.GetString(1)), cancellationToken);
    }

    public async Task<int> DeactivatePackageAsync(string packageId, string packageVersion, CancellationToken cancellationToken)
        => await SetActiveAsync(packageId, packageVersion, isActive: true, target: false, cancellationToken);

    public async Task<int> ReactivatePackageAsync(string packageId, string packageVersion, CancellationToken cancellationToken)
        => await SetActiveAsync(packageId, packageVersion, isActive: false, target: true, cancellationToken);

    private async Task<int> SetActiveAsync(
        string packageId, string packageVersion, bool isActive, bool target, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        // The current-state predicate matters: the returned count is rows *changed*, so a row already in the
        // target state is not counted.
        using var command = Command(
            $"UPDATE {QualifiedTable} SET {Packages.Column("IsActive").Name} = @target " +
            $"WHERE {Packages.Column("PackageId").Name} = @packageId " +
            $"AND {Packages.Column("PackageVersion").Name} = @packageVersion " +
            $"AND {Packages.Column("IsActive").Name} = @current");
        command.Parameters.AddWithValue("@target", target);
        command.Parameters.AddWithValue("@current", isActive);
        command.Parameters.AddWithValue("@packageId", packageId);
        command.Parameters.AddWithValue("@packageVersion", packageVersion);

        var count = await sqlExecutionService.ExecuteNonQueryAsync(connectionTenantId, command, cancellationToken);

        logger.LogInformation(
            "{Action} {Count} resources from package {PackageId}@{PackageVersion}",
            target ? "Reactivated" : "Deactivated", count, packageId, packageVersion);

        return count;
    }

    public async Task<int> DeletePackageAsync(string packageId, string packageVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        using var command = Command(
            $"DELETE FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("PackageId").Name} = @packageId " +
            $"AND {Packages.Column("PackageVersion").Name} = @packageVersion");
        command.Parameters.AddWithValue("@packageId", packageId);
        command.Parameters.AddWithValue("@packageVersion", packageVersion);

        var count = await sqlExecutionService.ExecuteNonQueryAsync(connectionTenantId, command, cancellationToken);

        logger.LogWarning(
            "Permanently deleted {Count} resources from package {PackageId}@{PackageVersion}",
            count, packageId, packageVersion);

        return count;
    }

    public async Task<IReadOnlyList<PackageResource>> GetStructureDefinitionsByCanonicalAsync(
        string canonical, string? fhirVersion = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        // fhirVersion is accepted and not applied -- see the class remarks. A caller passing "R4" cannot
        // match stored values like "4.0.1", so filtering on it would return nothing at all.
        //
        // A canonical containing '/' is treated as a full URL and matched exactly; a bare name matches any
        // canonical ending in "/name", so "Patient" finds
        // "http://hl7.org/fhir/StructureDefinition/Patient".
        var isFullUrl = canonical.Contains('/', StringComparison.Ordinal);
        var canonicalFilter = isFullUrl
            ? $"{Packages.Column("Canonical").Name} = @canonical"
            : $"{Packages.Column("Canonical").Name} LIKE @canonicalSuffix";

        using var command = Command(
            $"SELECT {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("ResourceType").Name} = 'StructureDefinition' AND {ActiveOnly} " +
            $"AND {canonicalFilter}");

        if (isFullUrl)
        {
            command.Parameters.AddWithValue("@canonical", canonical);
        }
        else
        {
            command.Parameters.AddWithValue("@canonicalSuffix", $"%/{EscapeLikePattern(canonical)}");
        }

        var rows = await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadResource, cancellationToken);

        return [.. rows.OrderByDescending(r => r.PackageVersion, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<PackageResource>> GetAllStructureDefinitionsAsync(
        string? fhirVersion = null, CancellationToken cancellationToken = default)
    {
        // fhirVersion accepted and not applied -- see the class remarks.
        var rows = await ByResourceTypeAsync("StructureDefinition", cancellationToken);

        logger.LogDebug(
            "Retrieved {Count} StructureDefinitions from packages (FHIR version: {FhirVersion})",
            rows.Count, fhirVersion ?? "any");

        return rows;
    }

    public async Task<bool> PackageVersionExistsAsync(
        string packageId, string packageVersion, int tenantId = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        // tenantId is accepted and cannot be applied: dbo.PackageResource has no tenant column at
        // all, because package content is global -- which is also why the repository is bound to the shared
        // database. ImplementationGuideProvider passes a real tenant id and logs "already loaded for tenant
        // {TenantId}", so it believes packages are per-tenant; they are not. Making that true is a schema
        // change, not a data-access change.
        using var command = Command(
            $"SELECT TOP 1 1 FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("PackageId").Name} = @packageId " +
            $"AND {Packages.Column("PackageVersion").Name} = @packageVersion AND {ActiveOnly}");
        command.Parameters.AddWithValue("@packageId", packageId);
        command.Parameters.AddWithValue("@packageVersion", packageVersion);

        // connectionTenantId, never the tenantId argument: that argument names a tenant this table has no
        // column for, and passing it here would try to open a connection to a tenant that may not exist.
        var rows = await sqlExecutionService.ExecuteReaderAsync(
            connectionTenantId, command, reader => reader.GetInt32(0), cancellationToken);

        return rows.Count > 0;
    }

    public async Task<IReadOnlySet<string>> GetCustomResourceTypesAsync(
        string? fhirVersion = null, CancellationToken cancellationToken = default)
    {
        var customTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // fhirVersion accepted and not applied -- see the class remarks.
            var structureDefinitions = await ByResourceTypeAsync("StructureDefinition", cancellationToken);

            foreach (var sd in structureDefinitions)
            {
                AddCustomTypeFrom(sd, customTypes);
            }

            if (customTypes.Count > 0)
            {
                logger.LogInformation(
                    "Extracted {Count} custom resource types from {StructureDefinitionCount} StructureDefinition resources",
                    customTypes.Count, structureDefinitions.Count);
            }

            return customTypes;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting custom resource types from packages");
            return new HashSet<string>();
        }
    }

    private void AddCustomTypeFrom(PackageResource sd, HashSet<string> customTypes)
    {
        try
        {
            var sdNode = JsonSourceNodeFactory.Parse<StructureDefinition>(sd.ResourceJson);

            if (!string.Equals(sdNode.ResourceType, "StructureDefinition", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Expected resourceType='StructureDefinition', got '{ResourceType}' in package {PackageId}@{PackageVersion}",
                    sdNode.ResourceType, sd.PackageId, sd.PackageVersion);
                return;
            }

            // A new resource type is either a specialization of kind 'resource', or a logical model.
            var isCustomType =
                (sdNode.Kind == StructureDefinitionKind.Resource && sdNode.Derivation == TypeDerivationRule.Specialization)
                || sdNode.Kind == StructureDefinitionKind.Logical;

            if (isCustomType && !string.IsNullOrWhiteSpace(sdNode.Name))
            {
                customTypes.Add(sdNode.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Failed to parse StructureDefinition from package {PackageId}@{PackageVersion}",
                sd.PackageId, sd.PackageVersion);
        }
    }

    public async Task<IReadOnlyList<PackageResource>> GetSearchParametersByResourceTypeAsync(
        string resourceType, string? fhirVersion = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        // fhirVersion accepted and not applied -- see the class remarks. The base[] filter stays in memory:
        // it is a JSON array membership test, not something the schema indexes.
        var all = await ByResourceTypeAsync("SearchParameter", cancellationToken);

        return [.. all.Where(r => SearchParameterAppliesToResourceType(r.ResourceJson, resourceType))];
    }

    public async Task<IReadOnlyList<PackageResource>> GetSearchParametersByCanonicalAsync(
        string canonical, string? fhirVersion = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        // fhirVersion accepted and not applied -- see the class remarks.
        using var command = Command(
            $"SELECT {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("ResourceType").Name} = 'SearchParameter' " +
            $"AND {Packages.Column("Canonical").Name} = @canonical AND {ActiveOnly} " +
            $"ORDER BY {Packages.Column("PackageVersion").Name} DESC");
        command.Parameters.AddWithValue("@canonical", canonical);

        return await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadResource, cancellationToken);
    }

    public async Task<IReadOnlyList<PackageResource>> GetAllSearchParametersAsync(
        string? fhirVersion = null, CancellationToken cancellationToken = default)
    {
        // fhirVersion accepted and not applied -- see the class remarks.
        return await ByResourceTypeAsync("SearchParameter", cancellationToken);
    }

    public async Task<IReadOnlyList<PackageResource>> GetOperationDefinitionsAsync(
        IReadOnlyList<string> operationNames, string? fhirVersion = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationNames);

        if (operationNames.Count == 0)
        {
            return [];
        }

        // fhirVersion accepted and not applied -- see the class remarks.
        var parameterNames = new StringBuilder();
        using var command = Command(string.Empty);

        for (var i = 0; i < operationNames.Count; i++)
        {
            var name = $"@name{i.ToString(CultureInfo.InvariantCulture)}";
            if (i > 0)
            {
                parameterNames.Append(", ");
            }

            parameterNames.Append(name);
            command.Parameters.AddWithValue(name, operationNames[i]);
        }

#pragma warning disable CA2100
        command.CommandText =
            $"SELECT {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("ResourceType").Name} = 'OperationDefinition' AND {ActiveOnly} " +
            $"AND {Packages.Column("ResourceId").Name} IN ({parameterNames}) " +
            $"ORDER BY {Packages.Column("PackageVersion").Name} DESC";
#pragma warning restore CA2100

        return await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadResource, cancellationToken);
    }

    public async Task<PackageResource?> GetStructureMapByUrlAsync(string canonicalUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalUrl);

        // An unparseable or non-http(s) canonical is "not found" rather than an error: callers resolve
        // arbitrary references through here and a malformed one is a miss, not a fault.
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("Invalid canonical URL format for StructureMap lookup: {CanonicalUrl}", canonicalUrl);
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogWarning("Canonical URL must use http or https scheme: {CanonicalUrl}", canonicalUrl);
            return null;
        }

        using var command = Command(
            $"SELECT TOP 1 {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("ResourceType").Name} = 'StructureMap' " +
            $"AND {Packages.Column("Canonical").Name} = @canonical AND {ActiveOnly} " +
            $"ORDER BY {Packages.Column("LoadedDate").Name} DESC");
        command.Parameters.AddWithValue("@canonical", canonicalUrl);

        return await SingleAsync(command, cancellationToken);
    }

    public async Task<PackageResource[]> GetResourcesForActivationAsync(
        string packageId, string version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        using var command = Command(
            $"SELECT {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("PackageId").Name} = @packageId " +
            $"AND {Packages.Column("PackageVersion").Name} = @packageVersion AND {ActiveOnly} " +
            $"AND {Packages.Column("ResourceType").Name} IN ('SearchParameter', 'StructureDefinition')");
        command.Parameters.AddWithValue("@packageId", packageId);
        command.Parameters.AddWithValue("@packageVersion", version);

        var rows = await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadResource, cancellationToken);
        return [.. rows];
    }

    private static string ActiveOnly => $"{Packages.Column("IsActive").Name} = 1";

    private async Task<IReadOnlyList<PackageResource>> ByResourceTypeAsync(string resourceType, CancellationToken cancellationToken)
    {
        using var command = Command(
            $"SELECT {SelectColumns} FROM {QualifiedTable} " +
            $"WHERE {Packages.Column("ResourceType").Name} = @resourceType AND {ActiveOnly} " +
            $"ORDER BY {Packages.Column("PackageVersion").Name} DESC");
        command.Parameters.AddWithValue("@resourceType", resourceType);

        return await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadResource, cancellationToken);
    }

    private async Task<PackageResource?> SingleAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        var rows = await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadResource, cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    private bool SearchParameterAppliesToResourceType(string resourceJson, string resourceType)
    {
        try
        {
            using var document = JsonDocument.Parse(resourceJson);

            if (!document.RootElement.TryGetProperty("base", out var baseElement)
                || baseElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return baseElement.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String
                && string.Equals(item.GetString(), resourceType, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse SearchParameter JSON to check base[] field");
            return false;
        }
    }

    // The six terminology-import columns are deliberately not read: the EF implementation's own
    // MapEntityToModel mapped these same eleven and left TerminologyImportStatus, ContentHash and the four
    // Import* fields null on every returned model, so callers have never seen them populated from here.
    private static PackageResource ReadResource(SqlDataReader reader) => new()
    {
        PackageResourceId = reader.GetInt64(0),
        PackageId = reader.GetString(1),
        PackageVersion = reader.GetString(2),
        ResourceType = reader.GetString(3),
        Canonical = reader.GetString(4),
        Version = reader.IsDBNull(5) ? null : reader.GetString(5),
        ResourceId = reader.GetString(6),
        ResourceJson = reader.GetString(7),
        FhirVersion = reader.GetString(8),
        LoadedDate = reader.GetDateTimeOffset(9),
        IsActive = reader.GetBoolean(10),
    };

    // 2601 duplicate key on a unique index, 2627 unique-constraint violation.
    private static bool IsDuplicateKey(SqlException ex) => ex.Number is 2601 or 2627;

    private static string EscapeLikePattern(string value)
        => value.Replace("[", "[[]", StringComparison.Ordinal)
                .Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal);
}
