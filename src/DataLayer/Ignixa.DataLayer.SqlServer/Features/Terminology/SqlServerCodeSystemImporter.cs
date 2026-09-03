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
/// links, final status — rather than that sequence being composed client-side over
/// <see cref="ISqlExecutionService.ExecuteInTransactionAsync{TResult}"/>. Either would be atomic; one stored
/// procedure call is one round trip instead of several, which matters when the payload is a whole
/// CodeSystem's worth of concepts. The stored procedures already own their transactions correctly, so this
/// stays a redesign of the insert path, not the transaction boundary.
/// </para>
/// <para>
/// ValueSet and ConceptMap import follow the same shape — build the rows client-side, hand them to one
/// procedure as a table-valued parameter — and carry their own corrections. Both previously resolved a
/// missing system URI to id <c>0</c>, which no row in <c>dbo.System</c> has, so a <c>contains</c> entry
/// without a system or a <c>group</c> without a target failed the entire import on a foreign key violation
/// reported as an opaque SQL error. See <see cref="SqlServerValueSetComposer"/> for the compose-side fixes.
/// </para>
/// </summary>
public sealed class SqlServerCodeSystemImporter(
    ISqlExecutionService sqlExecutionService,
    int systemPartitionId,
    ISystemRepository systemRepository,
    ILogger<SqlServerCodeSystemImporter> logger,
    int commandTimeoutSeconds = SqlServerOptions.DefaultTerminologyImportCommandTimeoutSeconds) : ITerminologyImporter
{
    private const int DefinitionMaxLength = 4000;

    private static readonly TableDescriptor Packages = SqlCatalog.Default.Table("PackageResource");

    public Task<TerminologyImportResult> ImportCodeSystemAsync(
        int tenantId, PackageResource packageResource, CancellationToken cancellationToken)
        => ImportAsync(tenantId, packageResource, "CodeSystem", ImportCodeSystemCoreAsync, cancellationToken);

    public Task<TerminologyImportResult> ImportValueSetAsync(
        int tenantId, PackageResource packageResource, CancellationToken cancellationToken)
        => ImportAsync(tenantId, packageResource, "ValueSet", ImportValueSetCoreAsync, cancellationToken);

    public Task<TerminologyImportResult> ImportConceptMapAsync(
        int tenantId, PackageResource packageResource, CancellationToken cancellationToken)
        => ImportAsync(tenantId, packageResource, "ConceptMap", ImportConceptMapCoreAsync, cancellationToken);

    /// <summary>
    /// The part all three imports share: reject the wrong resource type, require the package row, skip
    /// unchanged content, and turn any failure into a status on the package row rather than an exception.
    /// </summary>
    private async Task<TerminologyImportResult> ImportAsync(
        int tenantId,
        PackageResource packageResource,
        string resourceType,
        Func<PackageResource, JsonObject, string, CancellationToken, Task<TerminologyImportResult>> import,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        if (packageResource.ResourceType != resourceType)
        {
            throw new ArgumentException(
                $"Expected ResourceType '{resourceType}', got '{packageResource.ResourceType}'", nameof(packageResource));
        }

        logger.LogInformation(
            "Starting {ResourceType} import for '{Canonical}' (PackageResourceId: {PackageResourceId})",
            resourceType, packageResource.Canonical, packageResource.PackageResourceId);

        // Outside the try deliberately: with no package row there is nothing to record a failure onto, so
        // this is the one error the caller sees as an exception.
        var existing = await ReadPackageRowAsync(packageResource.PackageResourceId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"PackageResource {packageResource.PackageResourceId} not found in tenant {tenantId}");

        try
        {
            var resource = ParseResourceJson(packageResource.ResourceJson, resourceType);
            var contentHash = packageResource.ComputeContentHash();

            // Unchanged content that already reached a terminal outcome is skipped before any write, which is
            // what keeps re-loading a package from multiplying its rows.
            if (string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal)
                && TerminalStatusOf(existing.Status) is { } retainedStatus)
            {
                logger.LogInformation(
                    "{ResourceType} '{Canonical}' content unchanged (hash: {Hash}), retaining status {Status}",
                    resourceType, packageResource.Canonical, contentHash, retainedStatus);

                return TerminologyImportResult.CreateUnchanged(retainedStatus);
            }

            return await import(packageResource, resource, contentHash, cancellationToken);
        }
        catch (Exception ex)
        {
            // Failures are reported on the package row rather than thrown, matching the EF implementation:
            // the status column is the error channel, and a caller that ignores the result sees nothing.
            logger.LogError(
                ex,
                "Failed to import {ResourceType} '{Canonical}' (PackageResourceId: {PackageResourceId}): {ErrorMessage}",
                resourceType, packageResource.Canonical, packageResource.PackageResourceId, ex.Message);

            await RecordFailureAsync(packageResource.PackageResourceId, ex, cancellationToken);

            return TerminologyImportResult.CreateFailure(ex.Message);
        }
    }

    private async Task<TerminologyImportResult> ImportCodeSystemCoreAsync(
        PackageResource packageResource, JsonObject codeSystem, string contentHash, CancellationToken cancellationToken)
    {
        var metadata = ExtractMetadata(codeSystem);

        var (skip, skipReason) = SkipDecisionFor(metadata.Content);
        if (skip)
        {
            logger.LogInformation(
                "CodeSystem '{Canonical}' has content={Content}, skipping import",
                packageResource.Canonical, metadata.Content);

            await RecordSkippedAsync(packageResource.PackageResourceId, contentHash, skipReason, cancellationToken);

            return TerminologyImportResult.CreateSkipped();
        }

        var systemId = await systemRepository.GetOrCreateAsync(metadata.Url, cancellationToken);
        var concepts = FlattenConcepts(codeSystem["concept"]?.AsArray());

        logger.LogInformation(
            "Importing {ConceptCount} concepts for CodeSystem '{Canonical}'",
            concepts.Count, packageResource.Canonical);

        await ExecuteCodeSystemImportAsync(packageResource, contentHash, systemId, metadata, concepts, cancellationToken);

        return TerminologyImportResult.CreateSuccess(concepts.Count);
    }

    /// <summary>
    /// The two statuses that mean "this content has already been dealt with", and so make an unchanged hash
    /// sufficient to do nothing.
    /// <para>
    /// <c>Skipped</c> belongs here alongside <c>Completed</c>. A resource skipped for its own sake — a
    /// CodeSystem with <c>content=not-present</c>, or a supplement — has its hash stamped by
    /// <see cref="RecordSkippedAsync"/> and will never import no matter how often it is retried. Matching
    /// only <c>Completed</c> meant re-parsing and re-deciding every one of those on every package load.
    /// </para>
    /// </summary>
    private static TerminologyImportStatus? TerminalStatusOf(string? status) => status switch
    {
        nameof(TerminologyImportStatus.Completed) => TerminologyImportStatus.Completed,
        nameof(TerminologyImportStatus.Skipped) => TerminologyImportStatus.Skipped,
        _ => null,
    };

    /// <summary>
    /// Two <c>content</c> values mean "do not build a CodeSystem from this".
    /// <para>
    /// <c>not-present</c> declares that the concepts live elsewhere, so there is nothing to import.
    /// <c>supplement</c> is the load-bearing one: a supplement adds properties to concepts belonging to
    /// <b>another</b> CodeSystem and carries that CodeSystem's <c>url</c>. Importing it as an ordinary
    /// CodeSystem would put a second <c>dbo.TermCodeSystem</c> row under the same <c>SystemId</c>, and
    /// <c>LookupCodeAsync</c> resolves ties by <c>ImportedDate DESC</c> — so the supplement would shadow the
    /// real CodeSystem's concepts. Merging supplements is unimplemented in both implementations; skipping
    /// them is what keeps that from silently corrupting lookups.
    /// </para>
    /// </summary>
    private static (bool Skip, string? Reason) SkipDecisionFor(string content) => content switch
    {
        "not-present" => (true, null),
        "supplement" => (true, "Supplement import not yet implemented"),
        _ => (false, null),
    };

    /// <summary>
    /// A pre-computed <c>expansion</c> wins over <c>compose</c>, which is what makes an import cheap for the
    /// packages that ship one. Neither present is legal and produces a ValueSet row with no codes.
    /// </summary>
    private async Task<TerminologyImportResult> ImportValueSetCoreAsync(
        PackageResource packageResource, JsonObject valueSet, string contentHash, CancellationToken cancellationToken)
    {
        var metadata = ExtractValueSetMetadata(valueSet);

        IReadOnlyList<ValueSetExpansionRow> entries = [];
        var isPartial = false;
        string? partialReason = null;

        if (valueSet["expansion"] is JsonObject expansion)
        {
            entries = await BuildExpansionRowsAsync(expansion, cancellationToken);
        }
        else if (valueSet["compose"] is JsonObject compose)
        {
            var composed = await SqlServerValueSetComposer.ComposeAsync(
                compose, sqlExecutionService, systemPartitionId, systemRepository, logger, commandTimeoutSeconds, cancellationToken);

            entries = composed.Entries;
            isPartial = composed.IsPartial;
            partialReason = composed.PartialReason;

            if (isPartial)
            {
                logger.LogWarning(
                    "ValueSet '{Canonical}' has partial expansion ({Count} codes). Reason: {Reason}",
                    packageResource.Canonical, entries.Count, partialReason);
            }
        }

        await ExecuteValueSetImportAsync(
            packageResource, contentHash, metadata, entries, isPartial, partialReason, cancellationToken);

        return TerminologyImportResult.CreateSuccess(entries.Count);
    }

    private async Task<TerminologyImportResult> ImportConceptMapCoreAsync(
        PackageResource packageResource, JsonObject conceptMap, string contentHash, CancellationToken cancellationToken)
    {
        var metadata = ExtractConceptMapMetadata(conceptMap);
        var elements = await BuildConceptMapElementsAsync(conceptMap, cancellationToken);

        await ExecuteConceptMapImportAsync(packageResource, contentHash, metadata, elements, cancellationToken);

        return TerminologyImportResult.CreateSuccess(elements.Count);
    }

    private async Task ExecuteCodeSystemImportAsync(
        PackageResource packageResource,
        string contentHash,
        int systemId,
        CodeSystemMetadata metadata,
        IReadOnlyList<ConceptRow> concepts,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("dbo.ImportTermCodeSystem")
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = commandTimeoutSeconds,
        };

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

    private async Task ExecuteValueSetImportAsync(
        PackageResource packageResource,
        string contentHash,
        ValueSetMetadata metadata,
        IReadOnlyList<ValueSetExpansionRow> entries,
        bool isPartialExpansion,
        string? partialExpansionReason,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("dbo.ImportTermValueSet")
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = commandTimeoutSeconds,
        };

        command.Parameters.AddWithValue("@PackageResourceId", packageResource.PackageResourceId);
        command.Parameters.AddWithValue("@Canonical", metadata.Url);
        command.Parameters.AddWithValue("@Version", (object?)metadata.Version ?? DBNull.Value);
        command.Parameters.AddWithValue("@Name", metadata.Name);
        command.Parameters.AddWithValue("@Immutable", metadata.Immutable);
        command.Parameters.AddWithValue("@IsPartialExpansion", isPartialExpansion);
        command.Parameters.AddWithValue("@PartialExpansionReason", (object?)partialExpansionReason ?? DBNull.Value);

        var entriesParameter = command.Parameters.AddWithValue("@Entries", BuildExpansionTable(entries));
        entriesParameter.SqlDbType = SqlDbType.Structured;
        entriesParameter.TypeName = "dbo.TermValueSetExpansionList";

        await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.GetInt64(0), cancellationToken);

        await StampContentHashAsync(packageResource.PackageResourceId, contentHash, cancellationToken);
    }

    private async Task ExecuteConceptMapImportAsync(
        PackageResource packageResource,
        string contentHash,
        ConceptMapMetadata metadata,
        IReadOnlyList<ConceptMapElementRow> elements,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("dbo.ImportTermConceptMap")
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = commandTimeoutSeconds,
        };

        command.Parameters.AddWithValue("@PackageResourceId", packageResource.PackageResourceId);
        command.Parameters.AddWithValue("@Canonical", metadata.Url);
        command.Parameters.AddWithValue("@Version", (object?)metadata.Version ?? DBNull.Value);
        command.Parameters.AddWithValue("@Name", metadata.Name);
        command.Parameters.AddWithValue("@SourceCanonical", (object?)metadata.SourceCanonical ?? DBNull.Value);
        command.Parameters.AddWithValue("@TargetCanonical", (object?)metadata.TargetCanonical ?? DBNull.Value);

        var elementsParameter = command.Parameters.AddWithValue("@Elements", BuildConceptMapElementTable(elements));
        elementsParameter.SqlDbType = SqlDbType.Structured;
        elementsParameter.TypeName = "dbo.TermConceptMapElementList";

        await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.GetInt64(0), cancellationToken);

        await StampContentHashAsync(packageResource.PackageResourceId, contentHash, cancellationToken);
    }

    private static DataTable BuildExpansionTable(IReadOnlyList<ValueSetExpansionRow> entries)
    {
        var table = new DataTable();
        table.Columns.Add("SystemId", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("Display", typeof(string));
        table.Columns.Add("SystemVersion", typeof(string));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns.Add("Ordinal", typeof(int));

        for (var ordinal = 0; ordinal < entries.Count; ordinal++)
        {
            var entry = entries[ordinal];

            table.Rows.Add(
                entry.SystemId,
                entry.Code,
                (object?)entry.Display ?? DBNull.Value,
                (object?)entry.SystemVersion ?? DBNull.Value,
                true,
                ordinal);
        }

        return table;
    }

    private static DataTable BuildConceptMapElementTable(IReadOnlyList<ConceptMapElementRow> elements)
    {
        var table = new DataTable();
        table.Columns.Add("SourceSystemId", typeof(int));
        table.Columns.Add("SourceCode", typeof(string));
        table.Columns.Add("SourceDisplay", typeof(string));
        table.Columns.Add("TargetSystemId", typeof(int));
        table.Columns.Add("TargetCode", typeof(string));
        table.Columns.Add("TargetDisplay", typeof(string));
        table.Columns.Add("Equivalence", typeof(string));
        table.Columns.Add("Comment", typeof(string));
        table.Columns.Add("GroupIndex", typeof(int));

        foreach (var element in elements)
        {
            table.Rows.Add(
                element.SourceSystemId,
                element.SourceCode,
                (object?)element.SourceDisplay ?? DBNull.Value,
                (object?)element.TargetSystemId ?? DBNull.Value,
                (object?)element.TargetCode ?? DBNull.Value,
                (object?)element.TargetDisplay ?? DBNull.Value,
                element.Equivalence,
                (object?)element.Comment ?? DBNull.Value,
                element.GroupIndex);
        }

        return table;
    }

    /// <summary>
    /// Flattens <c>expansion.contains</c> breadth-first. The EF implementation read only the top level, so a
    /// hierarchical expansion — a grouping entry with its real codes nested underneath — imported as the
    /// groupers alone, or as nothing at all when they carried no code of their own.
    /// </summary>
    private async Task<IReadOnlyList<ValueSetExpansionRow>> BuildExpansionRowsAsync(
        JsonObject expansion, CancellationToken cancellationToken)
    {
        var rows = new List<ValueSetExpansionRow>();
        var queue = new Queue<JsonObject>(ObjectsOf(expansion["contains"]));

        while (queue.Count > 0)
        {
            var entry = queue.Dequeue();

            foreach (var child in ObjectsOf(entry["contains"]))
            {
                queue.Enqueue(child);
            }

            var code = entry["code"]?.GetValue<string>();
            var system = entry["system"]?.GetValue<string>();

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(system))
            {
                // A contains entry may legally carry only a display and nest its members underneath, and a
                // code without a system cannot be stored: TermValueSetExpansion.SystemId is a foreign key.
                // The EF implementation wrote id 0 for these, which no System row has, so one such entry
                // failed the whole import on a constraint violation.
                logger.LogWarning(
                    "Skipping expansion entry without a code or system: {Entry}", entry.ToJsonString());
                continue;
            }

            rows.Add(new ValueSetExpansionRow(
                SystemId: await systemRepository.GetOrCreateAsync(system, cancellationToken),
                Code: code,
                Display: entry["display"]?.GetValue<string>(),
                SystemVersion: entry["version"]?.GetValue<string>()));
        }

        return rows;
    }

    private async Task<IReadOnlyList<ConceptMapElementRow>> BuildConceptMapElementsAsync(
        JsonObject conceptMap, CancellationToken cancellationToken)
    {
        var rows = new List<ConceptMapElementRow>();
        var groupIndex = -1;

        foreach (var group in ObjectsOf(conceptMap["group"]))
        {
            groupIndex++;

            var source = group["source"]?.GetValue<string>();
            if (string.IsNullOrEmpty(source))
            {
                // TermConceptMapElement.SourceSystemId is NOT NULL and a foreign key, so a group without a
                // source has nowhere to put its elements. Stated here rather than left to fail as a
                // constraint violation on id 0, which is what the EF implementation produced.
                throw new InvalidOperationException(
                    $"ConceptMap.group[{groupIndex}].source is required: mapping elements cannot be stored without a source system");
            }

            var target = group["target"]?.GetValue<string>();

            rows.AddRange(BuildGroupElements(
                group,
                groupIndex,
                sourceSystemId: await systemRepository.GetOrCreateAsync(source, cancellationToken),
                // Null rather than a placeholder id when the group declares no target: the column is
                // nullable precisely because a mapping can name a target code whose system it never states.
                targetSystemId: string.IsNullOrEmpty(target)
                    ? null
                    : await systemRepository.GetOrCreateAsync(target, cancellationToken)));
        }

        return rows;
    }

    private IEnumerable<ConceptMapElementRow> BuildGroupElements(
        JsonObject group, int groupIndex, int sourceSystemId, int? targetSystemId)
    {
        foreach (var element in ObjectsOf(group["element"]))
        {
            var sourceCode = element["code"]?.GetValue<string>();

            if (string.IsNullOrEmpty(sourceCode))
            {
                logger.LogWarning("ConceptMap element missing source code, skipping");
                continue;
            }

            foreach (var row in BuildTargetRows(element, sourceCode, groupIndex, sourceSystemId, targetSystemId))
            {
                yield return row;
            }
        }
    }

    private static IEnumerable<ConceptMapElementRow> BuildTargetRows(
        JsonObject element, string sourceCode, int groupIndex, int sourceSystemId, int? targetSystemId)
    {
        var sourceDisplay = element["display"]?.GetValue<string>();
        var targets = ObjectsOf(element["target"]).ToList();

        if (targets.Count == 0)
        {
            // An element with no target is a code the map deliberately leaves unmapped, and is kept as a row
            // so the map can answer "no equivalent" rather than "never heard of it".
            yield return new ConceptMapElementRow(
                sourceSystemId, sourceCode, sourceDisplay, null, null, null, "unmatched", null, groupIndex);
            yield break;
        }

        foreach (var target in targets)
        {
            var targetCode = target["code"]?.GetValue<string>();

            yield return new ConceptMapElementRow(
                SourceSystemId: sourceSystemId,
                SourceCode: sourceCode,
                SourceDisplay: sourceDisplay,
                TargetSystemId: targetCode is null ? null : targetSystemId,
                TargetCode: targetCode,
                TargetDisplay: target["display"]?.GetValue<string>(),
                // R5 renamed equivalence to relationship. Reading both matches how source and target
                // canonicals are already read, and stops an R5 map storing every mapping as "equivalent".
                Equivalence: target["equivalence"]?.GetValue<string>()
                    ?? target["relationship"]?.GetValue<string>()
                    ?? "equivalent",
                Comment: target["comment"]?.GetValue<string>(),
                GroupIndex: groupIndex);
        }
    }

    private static IEnumerable<JsonObject> ObjectsOf(JsonNode? node)
        => node is JsonArray array ? array.OfType<JsonObject>() : [];

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
        // Whitespace counts as missing: content drives the skip decision below, and an empty string would
        // fall through it into a full import.
        Content: codeSystem["content"]?.GetValue<string>() is { } content && !string.IsNullOrWhiteSpace(content)
            ? content
            : throw new InvalidOperationException("CodeSystem.content is required"),
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

    /// <summary>
    /// Marks the package row Skipped and stamps the content hash, so the same unchanged content is not
    /// re-examined on every package load. The hash has to be written here as well as on the success path —
    /// without it a skipped CodeSystem is reconsidered forever.
    /// </summary>
    private async Task RecordSkippedAsync(
        long packageResourceId, string contentHash, string? reason, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"UPDATE {Packages.SchemaName}.{Packages.TableName} SET " +
            $"{Packages.Column("TerminologyImportStatus").Name} = 'Skipped', " +
            $"{Packages.Column("ContentHash").Name} = @contentHash, " +
            $"{Packages.Column("ImportStartDate").Name} = SYSDATETIMEOFFSET(), " +
            $"{Packages.Column("ImportCompletedDate").Name} = SYSDATETIMEOFFSET(), " +
            $"{Packages.Column("ImportErrorMessage").Name} = @reason " +
            $"WHERE {Packages.Column("PackageResourceId").Name} = @packageResourceId");
#pragma warning restore CA2100
        command.Parameters.AddWithValue("@contentHash", contentHash);
        command.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
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

    private static ValueSetMetadata ExtractValueSetMetadata(JsonObject valueSet) => new(
        Url: valueSet["url"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ValueSet.url is required"),
        Version: valueSet["version"]?.GetValue<string>(),
        // Mandatory here even though FHIR treats it as optional, because dbo.TermValueSet.Name is NOT NULL.
        Name: valueSet["name"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ValueSet.name is required"),
        Immutable: valueSet["immutable"]?.GetValue<bool>() ?? false);

    private static ConceptMapMetadata ExtractConceptMapMetadata(JsonObject conceptMap) => new(
        Url: conceptMap["url"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ConceptMap.url is required"),
        Version: conceptMap["version"]?.GetValue<string>(),
        Name: conceptMap["name"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ConceptMap.name is required"),
        // R4 spells these sourceUri/targetUri, R5 sourceCanonical/targetCanonical.
        SourceCanonical: conceptMap["sourceUri"]?.GetValue<string>()
            ?? conceptMap["sourceCanonical"]?.GetValue<string>(),
        TargetCanonical: conceptMap["targetUri"]?.GetValue<string>()
            ?? conceptMap["targetCanonical"]?.GetValue<string>());

    private sealed record ValueSetMetadata(string Url, string? Version, string Name, bool Immutable);

    private sealed record ConceptMapMetadata(
        string Url,
        string? Version,
        string Name,
        string? SourceCanonical,
        string? TargetCanonical);

    private sealed record ConceptMapElementRow(
        int SourceSystemId,
        string SourceCode,
        string? SourceDisplay,
        int? TargetSystemId,
        string? TargetCode,
        string? TargetDisplay,
        string Equivalence,
        string? Comment,
        int GroupIndex);

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
