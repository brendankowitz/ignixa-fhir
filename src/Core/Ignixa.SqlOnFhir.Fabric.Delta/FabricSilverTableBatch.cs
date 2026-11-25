// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Apache.Arrow;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// The resources to write to one Silver table, with the contract the table holds and the projection that turns each
/// resource into a row of it.
/// </summary>
public sealed partial class FabricSilverTableBatch
{
    /// <summary>
    /// Pairs a table with its contract, projection and resources, and checks they fit together and that the writer
    /// can merge the contract's schema.
    /// </summary>
    /// <param name="tablePath">The local table directory; relative paths resolve against the current directory.</param>
    /// <param name="contract">The Silver contract the table holds.</param>
    /// <param name="projection">The projection created from <paramref name="contract"/>.</param>
    /// <param name="resources">The resources to write, all of the contract's resource type; copied.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="tablePath"/> is blank; <paramref name="resources"/> holds a null; the projection was not created
    /// for this contract; or the contract has a column name outside <c>[A-Za-z_][A-Za-z0-9_-]*</c>, a column type the
    /// writer does not convert, or no timestamp <c>meta_lastUpdated</c> column.
    /// </exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public FabricSilverTableBatch(
        string tablePath,
        FabricSilverContract contract,
        FabricSilverProjection projection,
        IReadOnlyList<ResourceJsonNode> resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(resources);

        var columns = DeltaColumnType.ReadColumns(contract.Fields);
        var columnNames = columns.Select(column => column.Name).ToList();
        if (projection.ResourceType != contract.ResourceType || !projection.ColumnNames.SequenceEqual(columnNames, StringComparer.Ordinal))
        {
            throw new ArgumentException("The projection was not created from this contract.", nameof(projection));
        }

        if (columns.FirstOrDefault(column => !SqlSafeColumnName().IsMatch(column.Name)) is { } unsafeColumn)
        {
            throw new ArgumentException(
                $"The contract column at position {columns.IndexOf(unsafeColumn)} has a name the writer cannot quote.",
                nameof(contract));
        }

        if (!columns.Any(column => column.Name == HdsColumns.MetaLastUpdated && column.Type.Kind == DeltaTypeKind.Timestamp))
        {
            // The projection already requires a string id and a timestamp msftCreatedDatetime; it does not require
            // the flattened meta_lastUpdated the merge condition compares.
            throw new ArgumentException("The contract has no timestamp column 'meta_lastUpdated', which the merge depends on.", nameof(contract));
        }

        if (resources.Any(resource => resource is null))
        {
            throw new ArgumentException("The resources hold a null.", nameof(resources));
        }

        TablePath = Path.GetFullPath(tablePath);
        Contract = contract;
        Projection = projection;
        Resources = System.Array.AsReadOnly(resources.ToArray());
        Columns = columns;
        ArrowSchema = new Schema(columns.Select(column => column.ToArrowField()), metadata: null);
        LastUpdatedOrdinal = columnNames.IndexOf(HdsColumns.MetaLastUpdated);
        MergeStatement = HdsMergeStatement.Build(columnNames);
    }

    /// <summary>The full path of the table directory.</summary>
    public string TablePath { get; }

    /// <summary>The Silver contract the table holds.</summary>
    public FabricSilverContract Contract { get; }

    /// <summary>The projection that turns each resource into a row.</summary>
    public FabricSilverProjection Projection { get; }

    /// <summary>The resources to write, in batch order.</summary>
    public IReadOnlyList<ResourceJsonNode> Resources { get; }

    internal IReadOnlyList<DeltaColumn> Columns { get; }

    internal Schema ArrowSchema { get; }

    internal int LastUpdatedOrdinal { get; }

    internal string MergeStatement { get; }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SqlSafeColumnName();
}
