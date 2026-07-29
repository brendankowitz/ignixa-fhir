using System.Data;
using System.Text.Json.Nodes;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Ignixa.Search.Sql.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// <see cref="ITerminologyImporter"/> over <see cref="ISqlExecutionService"/>.
/// <para>
/// <b>CodeSystem import is a redesign, not a port.</b> The EF implementation chose between two client-side
/// insert paths — <c>AddRange</c> at or below 1,000 concepts, <c>SqlBulkCopy</c> above it — and ran a
/// separate parent-resolution pass on only the second. Every smaller CodeSystem therefore imported with a
/// flat hierarchy, and <c>$subsumes</c> answered "not-subsumed" for every pair in it while returning
/// well-formed FHIR. That defect is structural in the split, so the split is gone: one call to
/// <c>dbo.ImportTermCodeSystem</c> with the concepts as a table-valued parameter, which inserts and resolves
/// the hierarchy server-side in a single transaction.
/// </para>
/// <para>
/// The procedure also owns the whole sequence — status, delete-existing, code system row, concepts, parent
/// links, final status — because <see cref="ISqlExecutionService"/> has no transaction API and cannot span
/// calls. The EF version got atomicity from a transaction wrapping all of those steps; splitting them
/// client-side would let a failure leave a code system with no concepts.
/// </para>
/// <para>
/// <b>ValueSet and ConceptMap import are not yet ported.</b> Both are covered by the oracle but their
/// compose-expansion path is substantial, and this type is deliberately not registered anywhere until they
/// are done — production still resolves the EF importer. See the Phase F plan.
/// </para>
/// </summary>
public sealed class SqlServerCodeSystemImporter(
    ISqlExecutionService sqlExecutionService,
    int systemPartitionId,
    ISystemRepository systemRepository,
    ILogger<SqlServerCodeSystemImporter> logger) : ITerminologyImporter
{
    private const int DefinitionMaxLength = 4000;

    private static readonly TableDescriptor Packages = SqlCatalog.Default.Table("PackageResource");

    public async Task<TerminologyImportResult> ImportCodeSystemAsync(
        int tenantId, PackageResource packageResource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        if (packageResource.ResourceType != "CodeSystem")
        {
            throw new ArgumentException(
                $"Expected ResourceType 'CodeSystem', got '{packageResource.ResourceType}'", nameof(packageResource));
        }

        logger.LogInformation(
            "Starting CodeSystem import for '{Canonical}' (PackageResourceId: {PackageResourceId})",
            packageResource.Canonical, packageResource.PackageResourceId);

        var existing = await ReadPackageRowAsync(packageResource.PackageResourceId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"PackageResource {packageResource.PackageResourceId} not found in tenant {tenantId}");

        try
        {
            var codeSystem = ParseResourceJson(packageResource.ResourceJson, "CodeSystem");
            var contentHash = packageResource.ComputeContentHash();

            // Unchanged content that already completed is skipped before any write, which is what keeps
            // re-loading a package from multiplying its concepts.
            if (string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal)
                && string.Equals(existing.Status, nameof(TerminologyImportStatus.Completed), StringComparison.Ordinal))
            {
                logger.LogInformation(
                    "CodeSystem '{Canonical}' content unchanged (hash: {Hash}), skipping import",
                    packageResource.Canonical, contentHash);

                return TerminologyImportResult.CreateSkipped();
            }

            var metadata = ExtractMetadata(codeSystem);
            var systemId = await systemRepository.GetOrCreateAsync(metadata.Url, cancellationToken);
            var concepts = FlattenConcepts(codeSystem["concept"]?.AsArray());

            logger.LogInformation(
                "Importing {ConceptCount} concepts for CodeSystem '{Canonical}'",
                concepts.Count, packageResource.Canonical);

            await ExecuteImportAsync(packageResource, contentHash, systemId, metadata, concepts, cancellationToken);

            return TerminologyImportResult.CreateSuccess(concepts.Count);
        }
        catch (Exception ex)
        {
            // Failures are reported on the package row rather than thrown, matching the EF implementation:
            // the status column is the error channel, and a caller that ignores the result sees nothing.
            logger.LogError(
                ex,
                "Failed to import CodeSystem '{Canonical}' (PackageResourceId: {PackageResourceId}): {ErrorMessage}",
                packageResource.Canonical, packageResource.PackageResourceId, ex.Message);

            await RecordFailureAsync(packageResource.PackageResourceId, ex, cancellationToken);

            return TerminologyImportResult.CreateFailure(ex.Message);
        }
    }

    public Task<TerminologyImportResult> ImportValueSetAsync(
        int tenantId, PackageResource packageResource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        if (packageResource.ResourceType != "ValueSet")
        {
            throw new ArgumentException(
                $"Expected ResourceType 'ValueSet', got '{packageResource.ResourceType}'", nameof(packageResource));
        }

        throw new NotImplementedException(
            "ValueSet import is not ported yet (Phase F Task 6). This type is not registered anywhere; " +
            "SqlCodeSystemImporter still serves production.");
    }

    public Task<TerminologyImportResult> ImportConceptMapAsync(
        int tenantId, PackageResource packageResource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        if (packageResource.ResourceType != "ConceptMap")
        {
            throw new ArgumentException(
                $"Expected ResourceType 'ConceptMap', got '{packageResource.ResourceType}'", nameof(packageResource));
        }

        throw new NotImplementedException(
            "ConceptMap import is not ported yet (Phase F Task 6). This type is not registered anywhere; " +
            "SqlCodeSystemImporter still serves production.");
    }

    private async Task ExecuteImportAsync(
        PackageResource packageResource,
        string contentHash,
        int systemId,
        CodeSystemMetadata metadata,
        IReadOnlyList<ConceptRow> concepts,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("dbo.ImportTermCodeSystem") { CommandType = CommandType.StoredProcedure };

        command.Parameters.AddWithValue("@PackageResourceId", packageResource.PackageResourceId);
        command.Parameters.AddWithValue("@SystemId", systemId);
        command.Parameters.AddWithValue("@Version", (object?)metadata.Version ?? DBNull.Value);
        command.Parameters.AddWithValue("@ConceptCount", metadata.Count ?? concepts.Count);
        command.Parameters.AddWithValue("@Content", metadata.Content);
        command.Parameters.AddWithValue("@IsHierarchical", metadata.HierarchyMeaning is not null);
        command.Parameters.AddWithValue("@CaseSensitive", metadata.CaseSensitive);
        command.Parameters.AddWithValue("@Compositional", metadata.Compositional);

        var conceptsParameter = command.Parameters.AddWithValue("@Concepts", BuildConceptTable(concepts));
        conceptsParameter.SqlDbType = SqlDbType.Structured;
        conceptsParameter.TypeName = "dbo.TermConceptList";

        await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.GetInt64(0), cancellationToken);

        // The content hash is not the procedure's concern -- it is import bookkeeping rather than
        // terminology state -- so it is stamped separately once the import has committed.
        await StampContentHashAsync(packageResource.PackageResourceId, contentHash, cancellationToken);
    }

    private static DataTable BuildConceptTable(IReadOnlyList<ConceptRow> concepts)
    {
        var table = new DataTable();
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("Display", typeof(string));
        table.Columns.Add("Definition", typeof(string));
        table.Columns.Add("ParentCode", typeof(string));
        table.Columns.Add("Level", typeof(int));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns.Add("PropertiesJson", typeof(string));

        foreach (var concept in concepts)
        {
            table.Rows.Add(
                concept.Code,
                (object?)concept.Display ?? DBNull.Value,
                (object?)concept.Definition ?? DBNull.Value,
                (object?)concept.ParentCode ?? DBNull.Value,
                concept.Level,
                concept.IsActive,
                (object?)concept.PropertiesJson ?? DBNull.Value);
        }

        return table;
    }

    /// <summary>
    /// Flattens nested <c>concept[]</c> breadth-first, carrying each concept's parent <b>code</b>. Ids
    /// cannot be carried: they are assigned by the insert. The procedure resolves codes to ids server-side.
    /// </summary>
    private List<ConceptRow> FlattenConcepts(JsonArray? concepts)
    {
        var result = new List<ConceptRow>();

        if (concepts is null || concepts.Count == 0)
        {
            return result;
        }

        var queue = new Queue<(JsonObject Concept, string? ParentCode, int Level)>();

        foreach (var node in concepts)
        {
            if (node is JsonObject concept)
            {
                queue.Enqueue((concept, null, 0));
            }
        }

        while (queue.Count > 0)
        {
            var (concept, parentCode, level) = queue.Dequeue();

            var code = concept["code"]?.GetValue<string>();
            if (string.IsNullOrEmpty(code))
            {
                // A concept without a code is skipped rather than failing the import, so one malformed
                // entry does not cost the whole CodeSystem.
                logger.LogWarning("Skipping concept with missing code: {Concept}", concept.ToJsonString());
                continue;
            }

            var definition = concept["definition"]?.GetValue<string>();
            if (definition?.Length > DefinitionMaxLength)
            {
                logger.LogWarning(
                    "Truncating definition for concept '{Code}' from {Length} to {Max} characters",
                    code, definition.Length, DefinitionMaxLength);
                definition = definition[..DefinitionMaxLength];
            }

            result.Add(new ConceptRow(
                Code: code,
                Display: concept["display"]?.GetValue<string>(),
                Definition: definition,
                ParentCode: parentCode,
                Level: level,
                IsActive: true,
                PropertiesJson: SerializePropertiesJson(concept["property"], concept["designation"])));

            foreach (var childNode in concept["concept"]?.AsArray() ?? [])
            {
                if (childNode is JsonObject child)
                {
                    queue.Enqueue((child, code, level + 1));
                }
            }
        }

        return result;
    }

    private static string? SerializePropertiesJson(JsonNode? property, JsonNode? designation)
    {
        if (property is null && designation is null)
        {
            return null;
        }

        var wrapper = new JsonObject();

        if (property is not null)
        {
            wrapper["property"] = property.DeepClone();
        }

        if (designation is not null)
        {
            wrapper["designation"] = designation.DeepClone();
        }

        return wrapper.ToJsonString();
    }

    private static CodeSystemMetadata ExtractMetadata(JsonObject codeSystem) => new(
        Url: codeSystem["url"]?.GetValue<string>()
            ?? throw new InvalidOperationException("CodeSystem.url is required"),
        Version: codeSystem["version"]?.GetValue<string>(),
        Content: codeSystem["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("CodeSystem.content is required"),
        Count: codeSystem["count"]?.GetValue<int>(),
        CaseSensitive: codeSystem["caseSensitive"]?.GetValue<bool>() ?? true,
        HierarchyMeaning: codeSystem["hierarchyMeaning"]?.GetValue<string>(),
        Compositional: codeSystem["compositional"]?.GetValue<bool>() ?? false);

    private static JsonObject ParseResourceJson(string json, string expectedResourceType)
    {
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException($"{expectedResourceType} JSON is not an object");

        var resourceType = node["resourceType"]?.GetValue<string>();
        if (!string.Equals(resourceType, expectedResourceType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected resourceType '{expectedResourceType}', got '{resourceType ?? "null"}'");
        }

        return node;
    }

    private async Task<(string? ContentHash, string? Status)?> ReadPackageRowAsync(
        long packageResourceId, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"SELECT TOP 1 {Packages.Column("ContentHash").Name}, {Packages.Column("TerminologyImportStatus").Name} " +
            $"FROM {Packages.SchemaName}.{Packages.TableName} " +
            $"WHERE {Packages.Column("PackageResourceId").Name} = @packageResourceId");
#pragma warning restore CA2100
        command.Parameters.AddWithValue("@packageResourceId", packageResourceId);

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId,
            command,
            reader => (
                ContentHash: reader.IsDBNull(0) ? null : reader.GetString(0),
                Status: reader.IsDBNull(1) ? null : reader.GetString(1)),
            cancellationToken);

        return rows.Count > 0 ? rows[0] : null;
    }

    private async Task StampContentHashAsync(long packageResourceId, string contentHash, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"UPDATE {Packages.SchemaName}.{Packages.TableName} SET {Packages.Column("ContentHash").Name} = @contentHash " +
            $"WHERE {Packages.Column("PackageResourceId").Name} = @packageResourceId");
#pragma warning restore CA2100
        command.Parameters.AddWithValue("@contentHash", contentHash);
        command.Parameters.AddWithValue("@packageResourceId", packageResourceId);

        await sqlExecutionService.ExecuteNonQueryAsync(systemPartitionId, command, cancellationToken);
    }

    private async Task RecordFailureAsync(long packageResourceId, Exception ex, CancellationToken cancellationToken)
    {
        // ImportErrorMessage is NVARCHAR(1000). The EF version wrote message plus stack trace into it and
        // relied on a nested catch to swallow the resulting truncation, which loses the error entirely.
        // Truncating here keeps the message.
        var message = $"{ex.GetType().Name}: {ex.Message}";
        if (message.Length > 1000)
        {
            message = message[..1000];
        }

        try
        {
#pragma warning disable CA2100
            using var command = new SqlCommand(
                $"UPDATE {Packages.SchemaName}.{Packages.TableName} SET " +
                $"{Packages.Column("TerminologyImportStatus").Name} = 'Failed', " +
                $"{Packages.Column("ImportCompletedDate").Name} = SYSDATETIMEOFFSET(), " +
                $"{Packages.Column("ImportErrorMessage").Name} = @message " +
                $"WHERE {Packages.Column("PackageResourceId").Name} = @packageResourceId");
#pragma warning restore CA2100
            command.Parameters.AddWithValue("@message", message);
            command.Parameters.AddWithValue("@packageResourceId", packageResourceId);

            await sqlExecutionService.ExecuteNonQueryAsync(systemPartitionId, command, cancellationToken);
        }
        catch (SqlException saveEx)
        {
            // Recording the failure failed. Logged rather than rethrown so the original error is what the
            // caller sees, matching the EF implementation's nested handling.
            logger.LogError(saveEx, "Failed to record import failure for PackageResourceId {PackageResourceId}", packageResourceId);
        }
    }

    private sealed record ConceptRow(
        string Code,
        string? Display,
        string? Definition,
        string? ParentCode,
        int Level,
        bool IsActive,
        string? PropertiesJson);

    private sealed record CodeSystemMetadata(
        string Url,
        string? Version,
        string Content,
        int? Count,
        bool CaseSensitive,
        string? HierarchyMeaning,
        bool Compositional);
}
