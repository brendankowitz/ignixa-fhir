// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// The table contract of a Healthcare data solutions (HDS) v1.4.0 Silver table, read from a caller-supplied
/// Delta creation log: the exact schema and the validated protocol and properties a writer must reproduce.
/// Nothing is embedded or downloaded; the package stays with the caller.
/// </summary>
/// <remarks>
/// Instances are immutable and valid by construction: <see cref="LoadAsync"/> is the only way to create one and it
/// rejects any log that is not the supported contract.
/// </remarks>
public sealed class FabricSilverContract
{
    private const int ExpectedReaderVersion = 1;
    private const int ExpectedWriterVersion = 4;
    private const string ChangeDataFeedProperty = "delta.enableChangeDataFeed";
    private const string VOrderProperty = "delta.parquet.vorder.enabled";
    private const string LogFileName = "00000000000000000000.json";

    private static readonly string[] PackageSilverRoot =
        ["HDS.SourceCode", "hds-build-artifacts", "healthcare-tables", "1.4.0", "Silver"];

    private static readonly HashSet<string> SupportedResourceTypes = new(["Patient", "Observation"], StringComparer.Ordinal);

    private FabricSilverContract(
        string resourceType,
        string schemaString,
        ReadOnlyCollection<JsonElement> fields,
        int minReaderVersion,
        int minWriterVersion,
        ReadOnlyDictionary<string, string> configuration)
    {
        ResourceType = resourceType;
        SchemaString = schemaString;
        Fields = fields;
        MinReaderVersion = minReaderVersion;
        MinWriterVersion = minWriterVersion;
        Configuration = configuration;
    }

    /// <summary>The FHIR resource type of the table: <c>Patient</c> or <c>Observation</c>.</summary>
    public string ResourceType { get; }

    /// <summary>The Delta <c>schemaString</c> exactly as logged, character for character.</summary>
    public string SchemaString { get; }

    /// <summary>
    /// The top-level columns of <see cref="SchemaString"/> in schema order. Each element is the column's Delta
    /// schema field object (<c>name</c>, <c>type</c>, <c>nullable</c>, <c>metadata</c>), with nested struct, array,
    /// map and decimal types preserved exactly as logged.
    /// </summary>
    public IReadOnlyList<JsonElement> Fields { get; }

    /// <summary>The Delta protocol <c>minReaderVersion</c>; always 1.</summary>
    public int MinReaderVersion { get; }

    /// <summary>The Delta protocol <c>minWriterVersion</c>; always 4.</summary>
    public int MinWriterVersion { get; }

    /// <summary>
    /// The table properties as logged. Always contains change data feed and V-Order set to <c>true</c>; the values
    /// are kept as logged (casing included). The table is not partitioned.
    /// </summary>
    public IReadOnlyDictionary<string, string> Configuration { get; }

    /// <summary>
    /// Reads the creation log (version 0) of the Silver table for <paramref name="resourceType"/>.
    /// </summary>
    /// <param name="root">
    /// Either the Silver root, a directory with one subdirectory per resource type (<c>root/Patient/_delta_log/</c>),
    /// or the root of the unpacked HDS v1.4.0 package, whose Silver root is
    /// <c>HDS.SourceCode/hds-build-artifacts/healthcare-tables/1.4.0/Silver</c>. The Silver root form is used when
    /// the table exists directly under <paramref name="root"/>; otherwise the package form is used.
    /// </param>
    /// <param name="resourceType">An exact, case-sensitive <c>Patient</c> or <c>Observation</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The validated contract.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is blank or the resource type is not supported.</exception>
    /// <exception cref="FileNotFoundException">Neither layout holds a creation log for the resource type.</exception>
    /// <exception cref="InvalidDataException">
    /// The log is malformed, or is not the supported contract (protocol 1/4, change data feed and V-Order enabled,
    /// non-partitioned Parquet, a well-formed struct schema). The message never contains schema contents.
    /// </exception>
    /// <exception cref="IOException">The log cannot be read; nothing is retried or substituted.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<FabricSilverContract> LoadAsync(
        string root,
        string resourceType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!SupportedResourceTypes.Contains(resourceType))
        {
            throw new ArgumentException(
                $"Resource type is not supported; expected one of: {string.Join(", ", SupportedResourceTypes.Order())}.",
                nameof(resourceType));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var logPath = ResolveLogPath(root, resourceType);
        var (protocol, metaData) = await DeltaLogActions.ReadAsync(logPath, cancellationToken);

        var (reader, writer) = ReadProtocol(protocol);
        var schemaString = ReadMetaData(metaData, out var configuration);
        var fields = DeltaSchemaReader.ReadFields(schemaString);

        return new FabricSilverContract(resourceType, schemaString, fields, reader, writer, configuration);
    }

    private static string ResolveLogPath(string root, string resourceType)
    {
        var silverRootLog = Path.Combine(root, resourceType, "_delta_log", LogFileName);
        if (File.Exists(silverRootLog))
        {
            return silverRootLog;
        }

        var packageLog = Path.Combine([root, .. PackageSilverRoot, resourceType, "_delta_log", LogFileName]);
        return File.Exists(packageLog)
            ? packageLog
            : throw new FileNotFoundException(
                "No Silver creation log for the requested resource type was found under the supplied root.",
                silverRootLog);
    }

    private static (int Reader, int Writer) ReadProtocol(JsonElement protocol)
    {
        if (protocol.ValueKind != JsonValueKind.Object)
        {
            throw ContractError.Because("the protocol action is not an object.");
        }

        var reader = RequiredInt32(protocol, "minReaderVersion");
        var writer = RequiredInt32(protocol, "minWriterVersion");
        return (reader, writer) == (ExpectedReaderVersion, ExpectedWriterVersion)
            ? (reader, writer)
            : throw ContractError.Because(
                $"the protocol is reader {reader} / writer {writer}, not reader {ExpectedReaderVersion} / writer {ExpectedWriterVersion}.");
    }

    private static string ReadMetaData(JsonElement metaData, out ReadOnlyDictionary<string, string> configuration)
    {
        if (metaData.ValueKind != JsonValueKind.Object)
        {
            throw ContractError.Because("the metaData action is not an object.");
        }

        RequireParquetFormat(metaData);
        RequireNoPartitionColumns(metaData);
        configuration = ReadConfiguration(metaData);

        if (!metaData.TryGetProperty("schemaString", out var schemaString) || schemaString.ValueKind != JsonValueKind.String)
        {
            throw ContractError.Because("the metaData action has no schemaString.");
        }

        return schemaString.GetString()!;
    }

    private static void RequireParquetFormat(JsonElement metaData)
    {
        if (!metaData.TryGetProperty("format", out var format) ||
            format.ValueKind != JsonValueKind.Object ||
            !format.TryGetProperty("provider", out var provider) ||
            provider.ValueKind != JsonValueKind.String ||
            provider.GetString() != "parquet")
        {
            throw ContractError.Because("the table format is not Parquet.");
        }
    }

    private static void RequireNoPartitionColumns(JsonElement metaData)
    {
        if (!metaData.TryGetProperty("partitionColumns", out var partitionColumns) ||
            partitionColumns.ValueKind != JsonValueKind.Array ||
            partitionColumns.GetArrayLength() != 0)
        {
            throw ContractError.Because("the table is partitioned or its partitionColumns are missing.");
        }
    }

    private static ReadOnlyDictionary<string, string> ReadConfiguration(JsonElement metaData)
    {
        if (!metaData.TryGetProperty("configuration", out var node) || node.ValueKind != JsonValueKind.Object)
        {
            throw ContractError.Because("the metaData action has no configuration object.");
        }

        var configuration = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw ContractError.Because("a configuration value is not a string.");
            }

            if (!configuration.TryAdd(property.Name, property.Value.GetString()!))
            {
                throw ContractError.Because("the configuration repeats a property.");
            }
        }

        RequireEnabled(configuration, ChangeDataFeedProperty, "change data feed");
        RequireEnabled(configuration, VOrderProperty, "V-Order");
        return new ReadOnlyDictionary<string, string>(configuration);
    }

    private static void RequireEnabled(Dictionary<string, string> configuration, string property, string label)
    {
        if (!configuration.TryGetValue(property, out var value) ||
            !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw ContractError.Because($"{label} is not enabled in the table configuration.");
        }
    }

    private static int RequiredInt32(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw ContractError.Because($"the protocol has no integer {name}.");
}
