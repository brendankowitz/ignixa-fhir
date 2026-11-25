// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Projects one FHIR R4 resource at a time into a row of a Silver table, shaped only by that table's contract.
/// </summary>
/// <remarks>
/// <para>
/// Every column is filled by the first rule that applies: the lineage columns (<c>msftModifiedDatetime</c>,
/// <c>msftCreatedDatetime</c>, <c>msftSourceSystem</c>, <c>msftFilePath</c>) from the ingestion; <c>id</c> as the hashed
/// Silver id; <c>identifier</c> as the source identifiers plus a source-id identifier; <c>x_string</c> as the JSON text
/// of the final column <c>x</c>; <c>a_b</c>, where struct column <c>a</c> has a field <c>b</c>, as a copy of the final
/// <c>a.b</c>; <c>xOrig</c> as the source value of <c>x</c>; anything else from the same-name source property. Inside
/// values, every struct carrying <c>msftSourceReference</c> is a normalized Reference.
/// </para>
/// <para>
/// Instances are immutable and thread-safe, provided the schema is (the generated schema providers are). Each call
/// carries its own lineage, so one instance serves any number of sources concurrently.
/// </para>
/// </remarks>
public sealed class FabricSilverProjection
{
    private const string IdColumn = "id";
    private const string IdentifierColumn = "identifier";
    private const string ModifiedColumn = "msftModifiedDatetime";
    private const string CreatedColumn = "msftCreatedDatetime";
    private const string SourceSystemColumn = "msftSourceSystem";
    private const string FilePathColumn = "msftFilePath";
    private const string CompanionSuffix = "_string";

    private readonly ReadOnlyCollection<SilverField> _columns;
    private readonly ColumnPlan[] _plans;
    private readonly ReadOnlyCollection<string> _columnNames;
    private readonly IReadOnlyDictionary<string, int> _indexes;
    private readonly int[] _derivedOrder;
    private readonly SilverReferencePattern _pattern;

    private FabricSilverProjection(
        FabricSilverContract contract,
        ReadOnlyCollection<SilverField> columns,
        ColumnPlan[] plans,
        SilverReferencePattern pattern)
    {
        Contract = contract;
        ResourceType = contract.ResourceType;
        _columns = columns;
        _plans = plans;
        _pattern = pattern;

        // A derived column's base name is a strict prefix of its own, so shorter names first puts every base
        // (which may itself be derived) before its dependants, whatever the contract order.
        _derivedOrder = Enumerable.Range(0, plans.Length)
            .Where(index => plans[index].Role is ColumnRole.Companion or ColumnRole.Flattened)
            .OrderBy(index => columns[index].Name.Length)
            .ToArray();
        _columnNames = Array.AsReadOnly(columns.Select(column => column.Name).ToArray());
        _indexes = columns.Select((column, index) => (column.Name, index)).ToDictionary(StringComparer.Ordinal);
    }

    private enum ColumnRole
    {
        IngestedAt,
        SourceSystem,
        FilePath,
        Id,
        Identifier,
        Companion,
        Flattened,
        Field,
    }

    /// <summary>The contract the projection was created from.</summary>
    public FabricSilverContract Contract { get; }

    /// <summary>The FHIR resource type of the Silver table, as named by the contract.</summary>
    public string ResourceType { get; }

    /// <summary>The contract's column names in contract order.</summary>
    public IReadOnlyList<string> ColumnNames => _columnNames;

    /// <summary>Compiles the projection for a contract against the FHIR R4 schema.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> or <paramref name="schema"/> is null.</exception>
    /// <exception cref="ArgumentException">The schema is not FHIR R4.</exception>
    /// <exception cref="InvalidDataException">The contract has a column the projection cannot fill.</exception>
    public static FabricSilverProjection Create(FabricSilverContract contract, ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Version != FhirVersion.R4)
        {
            throw new ArgumentException("The Silver projection supports the FHIR R4 schema only.", nameof(schema));
        }

        var pattern = new SilverReferencePattern(schema);
        var columns = SilverType.ParseColumns(contract.Fields);
        var plans = new ColumnPlan[columns.Count];
        for (var index = 0; index < columns.Count; index++)
        {
            plans[index] = PlanColumn(columns, index);
        }

        foreach (var required in new[] { IdColumn, ModifiedColumn, CreatedColumn })
        {
            if (!columns.Any(column => column.Name == required))
            {
                throw ContractError.Because($"the schema lacks the required Silver column {required}.");
            }
        }

        return new FabricSilverProjection(contract, columns, plans, pattern);
    }

    /// <summary>Projects one resource.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> or <paramref name="ingestion"/> is null.</exception>
    /// <exception cref="FabricSilverProjectionException">
    /// The resource is of another type, has no id, has a <c>meta.lastUpdated</c> that is not a FHIR date, dateTime or
    /// instant with at most microsecond precision (checked even when the contract has no <c>meta</c> column), or holds
    /// a value the table's schema cannot store.
    /// </exception>
    public FabricSilverRow Project(ResourceJsonNode resource, FabricSilverIngestion ingestion)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(ingestion);

        // The raw JSON is read rather than the element tree: Silver leaves follow JSON-token semantics (number text,
        // structured text for extensions) that the element tree normalizes away.
        var root = ((IMutableJsonNode)resource).MutableNode;
        if (root["resourceType"] is not JsonValue type ||
            type.GetValueKind() != JsonValueKind.String ||
            type.GetValue<string>() != ResourceType)
        {
            throw SilverValueReader.Reject("resourceType", "does not match the Silver table");
        }

        if (root[IdColumn] is not JsonValue idValue ||
            idValue.GetValueKind() != JsonValueKind.String ||
            idValue.GetValue<string>().Length == 0)
        {
            throw SilverValueReader.Reject(IdColumn, "must be a non-empty string");
        }

        var sourceId = idValue.GetValue<string>();
        var id = SilverIdHash.Compute(ingestion.StrippedNamespace, ResourceType, sourceId);
        var reader = new SilverValueReader(new SilverReferenceNormalizer(_pattern, ingestion));
        var cells = new JsonNode?[_columns.Count];
        for (var index = 0; index < cells.Length; index++)
        {
            var column = _columns[index];
            cells[index] = _plans[index].Role switch
            {
                ColumnRole.IngestedAt => SilverTemporal.FormatTimestamp(ingestion.IngestedAt),
                ColumnRole.SourceSystem => ingestion.SourceNamespace,
                ColumnRole.FilePath => ingestion.FilePath,
                ColumnRole.Id => id,
                ColumnRole.Identifier => reader.Read(
                    SilverResourceIdentifier.Build(root[IdentifierColumn], column.Type.Element!, sourceId, ingestion, reader),
                    column.Type,
                    IdentifierColumn,
                    SilverValueReader.ReadMode.Source),
                ColumnRole.Field => reader.Read(root[column.SourceName], column.Type, column.SourceName, SilverValueReader.ReadMode.Source),
                _ => null,
            };
        }

        // Derived columns read the final values above, so they are filled in a second pass, bases first.
        foreach (var index in _derivedOrder)
        {
            var plan = _plans[index];
            cells[index] = plan.Role == ColumnRole.Companion
                ? SilverJsonText.Companion(cells[plan.BaseIndex], _columns[plan.BaseIndex].Type)
                : cells[plan.BaseIndex]?[plan.ChildName!]?.DeepClone();
        }

        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index] is null && !_columns[index].Nullable)
            {
                throw SilverValueReader.Reject(_columns[index].Name, "is required by the Silver schema");
            }
        }

        return new FabricSilverRow(
            ResourceType,
            _columnNames,
            _indexes,
            ToElements(cells),
            id,
            sourceId,
            ReadMetaLastUpdated(root),
            ingestion.IngestedAt,
            ingestion.IngestedAt);
    }

    private static DateTimeOffset? ReadMetaLastUpdated(JsonObject root)
    {
        const string Path = "meta.lastUpdated";
        if (root["meta"] is not JsonObject meta || meta["lastUpdated"] is not { } lastUpdated)
        {
            return null;
        }

        return lastUpdated.GetValueKind() == JsonValueKind.String &&
               SilverTemporal.TryParseTimestamp(lastUpdated.GetValue<string>(), out var instant)
            ? instant
            : throw SilverValueReader.Reject(Path, "must be a FHIR date, dateTime or instant with at most microsecond precision");
    }

    private static JsonElement[] ToElements(JsonNode?[] cells)
    {
        using var document = JsonDocument.Parse(new JsonArray(cells).ToJsonString());
        return document.RootElement.EnumerateArray().Select(cell => cell.Clone()).ToArray();
    }

    private static ColumnPlan PlanColumn(IReadOnlyList<SilverField> columns, int index)
    {
        var column = columns[index];
        var position = $"schema.fields[{index}]";
        switch (column.Name)
        {
            case ModifiedColumn or CreatedColumn:
                Require(column.Type.Kind == SilverTypeKind.Timestamp, position, "must be a timestamp");
                return new ColumnPlan(ColumnRole.IngestedAt);
            case SourceSystemColumn:
                Require(column.Type.Kind == SilverTypeKind.String, position, "must be a string");
                return new ColumnPlan(ColumnRole.SourceSystem);
            case FilePathColumn:
                Require(column.Type.Kind == SilverTypeKind.String, position, "must be a string");
                return new ColumnPlan(ColumnRole.FilePath);
            case IdColumn:
                Require(column.Type.Kind == SilverTypeKind.String, position, "must be a string");
                return new ColumnPlan(ColumnRole.Id);
            case IdentifierColumn:
                Require(
                    column.Type.Kind == SilverTypeKind.Array && column.Type.Element!.Kind == SilverTypeKind.Struct,
                    position,
                    "must be an array of structs");
                return new ColumnPlan(ColumnRole.Identifier);
        }

        if (column.Name.EndsWith(CompanionSuffix, StringComparison.Ordinal) &&
            FindColumn(columns, column.Name[..^CompanionSuffix.Length]) is { } companionOf)
        {
            Require(
                column.Type.Kind == SilverTypeKind.String && columns[companionOf].Type.IsComplex,
                position,
                "is a companion column, so it must be a string describing a struct or array column");
            return new ColumnPlan(ColumnRole.Companion, companionOf);
        }

        for (var underscore = column.Name.IndexOf('_', StringComparison.Ordinal);
             underscore > 0;
             underscore = column.Name.IndexOf('_', underscore + 1))
        {
            var parent = FindColumn(columns, column.Name[..underscore]);
            var childName = column.Name[(underscore + 1)..];
            if (parent is { } parentIndex && columns[parentIndex].Type.FindField(childName) is { } child)
            {
                Require(
                    child.Type.IsSameAs(column.Type),
                    position,
                    "flattens a struct field, so it must have that field's type");
                return new ColumnPlan(ColumnRole.Flattened, parentIndex, childName);
            }
        }

        return new ColumnPlan(ColumnRole.Field);
    }

    private static int? FindColumn(IReadOnlyList<SilverField> columns, string name)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index].Name == name)
            {
                return index;
            }
        }

        return null;
    }

    private static void Require(bool condition, string position, string reason)
    {
        if (!condition)
        {
            throw ContractError.Because($"{position} {reason}.");
        }
    }

    private sealed record ColumnPlan(ColumnRole Role, int BaseIndex = -1, string? ChildName = null);
}
