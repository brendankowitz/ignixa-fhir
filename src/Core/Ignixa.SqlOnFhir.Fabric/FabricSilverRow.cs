// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// One Silver row: a cell per contract column, in contract order. Immutable and safe to share across threads.
/// </summary>
/// <remarks>
/// Each cell is the column value in a JSON transit encoding: strings, booleans and integers as themselves; decimals
/// as plain-notation numbers with exactly the column scale; timestamps as UTC text with six fraction digits
/// (<c>yyyy-MM-ddTHH:mm:ss.ffffffZ</c>); dates as <c>yyyy-MM-dd</c>; structs as objects listing every field in schema
/// order (an absent field is an explicit null); arrays as arrays; and an absent value as null. A null struct is
/// therefore distinguishable from a present struct whose fields are all null.
/// </remarks>
public sealed class FabricSilverRow
{
    private const string CreatedColumn = "msftCreatedDatetime";

    private readonly ReadOnlyCollection<string> _columnNames;
    private readonly JsonElement[] _cells;
    private readonly IReadOnlyDictionary<string, int> _indexes;

    internal FabricSilverRow(
        string resourceType,
        ReadOnlyCollection<string> columnNames,
        IReadOnlyDictionary<string, int> indexes,
        JsonElement[] cells,
        string id,
        string idOrig,
        DateTimeOffset? metaLastUpdated,
        DateTimeOffset modifiedDatetime,
        DateTimeOffset createdDatetime)
    {
        ResourceType = resourceType;
        _columnNames = columnNames;
        _indexes = indexes;
        _cells = cells;
        Cells = Array.AsReadOnly(cells);
        Id = id;
        IdOrig = idOrig;
        MetaLastUpdated = metaLastUpdated;
        ModifiedDatetime = modifiedDatetime;
        CreatedDatetime = createdDatetime;
    }

    public string ResourceType { get; }

    public IReadOnlyList<string> ColumnNames => _columnNames;

    /// <summary>The cells, aligned with <see cref="ColumnNames"/>.</summary>
    public IReadOnlyList<JsonElement> Cells { get; }

    /// <summary>The hashed Silver id (the <c>id</c> column).</summary>
    public string Id { get; }

    /// <summary>The source resource id.</summary>
    public string IdOrig { get; }

    /// <summary>The source <c>meta.lastUpdated</c> in UTC at microsecond precision, or null when the resource has none.</summary>
    public DateTimeOffset? MetaLastUpdated { get; }

    /// <summary>The <c>msftModifiedDatetime</c> value: the ingestion instant.</summary>
    public DateTimeOffset ModifiedDatetime { get; }

    /// <summary>The <c>msftCreatedDatetime</c> value: the ingestion instant unless replaced by <see cref="WithCreatedDatetime"/>.</summary>
    public DateTimeOffset CreatedDatetime { get; }

    /// <exception cref="KeyNotFoundException">The contract has no column with this exact name.</exception>
    public JsonElement this[string columnName] => _cells[_indexes[columnName]];

    /// <summary>
    /// Returns a copy whose <c>msftCreatedDatetime</c> is the given instant, for a writer that keeps the creation time of
    /// a row it updates.
    /// </summary>
    /// <param name="createdDatetime">A UTC instant; finer than microsecond precision is truncated.</param>
    /// <exception cref="ArgumentException">The instant is not in UTC.</exception>
    public FabricSilverRow WithCreatedDatetime(DateTimeOffset createdDatetime)
    {
        if (createdDatetime.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The creation instant must be expressed in UTC.", nameof(createdDatetime));
        }

        var created = SilverTemporal.TruncateToMicroseconds(createdDatetime);
        var cells = (JsonElement[])_cells.Clone();
        cells[_indexes[CreatedColumn]] = JsonSerializer.SerializeToElement(SilverTemporal.FormatTimestamp(created));
        return new FabricSilverRow(
            ResourceType,
            _columnNames,
            _indexes,
            cells,
            Id,
            IdOrig,
            MetaLastUpdated,
            ModifiedDatetime,
            created);
    }
}
