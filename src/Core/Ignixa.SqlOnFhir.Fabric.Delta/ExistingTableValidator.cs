// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using DeltaLake.Table;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Checks the latest state of an existing table against the contract it must hold, failing closed on the first
/// difference. Checks run in a fixed order so the reported aspect is deterministic.
/// </summary>
internal static class ExistingTableValidator
{
    // delta.* properties that only tune checkpointing, retention, statistics or file sizing. Every other delta.*
    // property may add a writer obligation (append-only, CHECK constraints, column mapping, deletion vectors, row
    // tracking, table features) that this writer does not honor, so it fails closed.
    private static readonly HashSet<string> WriterNeutralProperties = new(
        [
            "delta.checkpointInterval",
            "delta.checkpoint.writeStatsAsJson",
            "delta.checkpoint.writeStatsAsStruct",
            "delta.dataSkippingNumIndexedCols",
            "delta.dataSkippingStatsColumns",
            "delta.deletedFileRetentionDuration",
            "delta.enableExpiredLogCleanup",
            "delta.logRetentionDuration",
            "delta.setTransactionRetentionDuration",
            "delta.targetFileSize",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <exception cref="FabricSilverTableMismatchException">The table does not match <paramref name="contract"/>.</exception>
    internal static void Validate(ProtocolInfo protocol, TableMetadata metadata, FabricSilverContract contract)
    {
        // Reader/writer feature lists exist only from reader 3 / writer 7, so exactly 1/4 also means no table features.
        if (protocol.MinimumReaderVersion != VersionZeroCommit.MinReaderVersion ||
            protocol.MinimumWriterVersion != VersionZeroCommit.MinWriterVersion)
        {
            throw Mismatch(
                FabricSilverTableAspect.Protocol,
                $"The table's Delta protocol is not reader {VersionZeroCommit.MinReaderVersion} / writer {VersionZeroCommit.MinWriterVersion}.");
        }

        // No format check: the engine reports provider "parquet" with no options whatever the log holds, so a check
        // here could never fail. Delta data files are Parquet for every reader regardless of the logged provider.

        // The engine returns its own re-serialization of the logged schema, not the logged text, so an ordinal
        // comparison would reject a table created from a contract whose schema is not in the engine's compact form.
        // Structural equality keeps every semantic difference (field order lives in arrays, which compare in order)
        // and ignores only whitespace and object member order.
        if (!JsonNode.DeepEquals(JsonNode.Parse(metadata.SchemaString), JsonNode.Parse(contract.SchemaString)))
        {
            throw Mismatch(FabricSilverTableAspect.Schema, "The table's schema differs from the contract's schema.");
        }

        if (metadata.PartitionColumns.Count != 0)
        {
            throw Mismatch(FabricSilverTableAspect.PartitionColumns, "The table is partitioned; the contract table is not.");
        }

        ValidateConfiguration(metadata.Configuration, contract.Configuration);
    }

    private static void ValidateConfiguration(
        IReadOnlyDictionary<string, string> table,
        IReadOnlyDictionary<string, string> contract)
    {
        if (!table.TryGetValue(VersionZeroCommit.ChangeDataFeedProperty, out var changeDataFeed) ||
            !string.Equals(changeDataFeed, VersionZeroCommit.ChangeDataFeedEnabled, StringComparison.Ordinal))
        {
            throw new FabricSilverTableMismatchException(
                FabricSilverTableAspect.ChangeDataFeed,
                VersionZeroCommit.ChangeDataFeedProperty,
                $"The table's change data feed setting is incompatible: {VersionZeroCommit.ChangeDataFeedProperty} must be " +
                $"exactly lowercase \"{VersionZeroCommit.ChangeDataFeedEnabled}\", because the Delta engine does not write " +
                "the change feed for any other spelling. The table is not corrected automatically; set the property " +
                "before writing.");
        }

        foreach (var (name, value) in contract)
        {
            if (name != VersionZeroCommit.ChangeDataFeedProperty &&
                (!table.TryGetValue(name, out var actual) || !string.Equals(actual, value, StringComparison.Ordinal)))
            {
                throw new FabricSilverTableMismatchException(
                    FabricSilverTableAspect.Configuration,
                    name,
                    $"The table property {name} is missing or does not have the contract's value.");
            }
        }

        if (table.Keys.Any(name => IsUndeclaredDeltaProperty(name, contract)))
        {
            throw Mismatch(
                FabricSilverTableAspect.UndeclaredProperty,
                "The table has a delta.* property that the contract does not declare and that may change what a writer " +
                "must honor.");
        }
    }

    private static bool IsUndeclaredDeltaProperty(string name, IReadOnlyDictionary<string, string> contract) =>
        name.StartsWith("delta.", StringComparison.OrdinalIgnoreCase) &&
        !contract.ContainsKey(name) &&
        !WriterNeutralProperties.Contains(name);

    private static FabricSilverTableMismatchException Mismatch(FabricSilverTableAspect aspect, string message) =>
        new(aspect, propertyName: null, message);
}
