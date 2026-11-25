// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// The table <b>creation template</b> of a Healthcare data solutions (HDS) v1.4.0 Silver table, read from a
/// caller-supplied Delta creation log: the exact schema and the validated protocol and properties a writer must
/// reproduce when it creates the table. Nothing is embedded or downloaded; the package stays with the caller.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a version 0 template and nothing more. Only <c>00000000000000000000.json</c> is read; later
/// commits, checkpoints and any <c>metaData</c> or <c>protocol</c> change made after creation are never resolved, so
/// the contract says nothing about the current state of an existing table. Use it to create tables, not to describe
/// them.
/// </para>
/// <para>
/// The contract validates what a protocol 1/4 writer can promise (see <see cref="LoadAsync"/>). Other
/// <see cref="Configuration"/> properties and column <c>metadata</c> are carried through exactly as logged, not
/// interpreted: a sink that writes the table must reproduce each of them faithfully or reject the contract, and must
/// not silently drop them.
/// </para>
/// <para>
/// Instances are immutable and valid by construction: <see cref="LoadAsync"/> is the only way to create one and it
/// rejects any log that is not the supported contract.
/// </para>
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

    private static readonly string[] ProtocolProperties = ["minReaderVersion", "minWriterVersion"];

    // Delta table features that need a protocol above reader 1 / writer 4 (column mapping, deletion vectors, row
    // tracking, v2 checkpoints, type widening, Iceberg compatibility, UniForm) plus explicit feature flags.
    private static readonly string[] UnsupportedConfigurationPrefixes =
    [
        "delta.feature.",
        "delta.columnMapping.",
        "delta.enableDeletionVectors",
        "delta.enableRowTracking",
        "delta.rowTracking.",
        "delta.checkpointPolicy",
        "delta.enableTypeWidening",
        "delta.enableIcebergCompat",
        "delta.universalFormat.",
    ];

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
    /// map and decimal types preserved exactly as logged. Field <c>metadata</c> is carried uninterpreted: the writer
    /// must reproduce it or reject the contract.
    /// </summary>
    public IReadOnlyList<JsonElement> Fields { get; }

    /// <summary>The Delta protocol <c>minReaderVersion</c>; always 1.</summary>
    public int MinReaderVersion { get; }

    /// <summary>The Delta protocol <c>minWriterVersion</c>; always 4.</summary>
    public int MinWriterVersion { get; }

    /// <summary>
    /// The table properties as logged, as a creation template. Always contains change data feed and V-Order set to
    /// <c>true</c> in any casing; values are kept as logged. Any property other than those two is carried through
    /// uninterpreted, so the writer must honor it or reject the contract. The table is not partitioned.
    /// </summary>
    public IReadOnlyDictionary<string, string> Configuration { get; }

    /// <summary>
    /// Reads the creation log (version 0 only; later commits and checkpoints are ignored) of the Silver table for
    /// <paramref name="resourceType"/>.
    /// </summary>
    /// <param name="root">
    /// Either the Silver root, a directory with one subdirectory per resource type (<c>root/Patient/_delta_log/</c>),
    /// or the root of the unpacked HDS v1.4.0 package, whose Silver root is
    /// <c>HDS.SourceCode/hds-build-artifacts/healthcare-tables/1.4.0/Silver</c>. Precedence is deterministic: the
    /// Silver root location is tried first and wins whenever it can be opened; the package location is tried only when
    /// the Silver root location does not exist (a missing file or directory). Any other failure to open the first
    /// location, such as access denied or an I/O error, is thrown rather than falling back, and the two layouts are
    /// never merged.
    /// </param>
    /// <param name="resourceType">An exact, case-sensitive <c>Patient</c> or <c>Observation</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The validated contract.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is blank or the resource type is not supported.</exception>
    /// <exception cref="FileNotFoundException">Neither layout holds a creation log for the resource type.</exception>
    /// <exception cref="InvalidDataException">
    /// The log is not strict UTF-8 without a byte order mark, is malformed or ambiguous (repeated property names,
    /// invalid string escapes), or is not the supported contract: protocol exactly reader 1 / writer 4 with no table
    /// feature lists, no table feature configuration, no column mapping or identity metadata, change data feed and
    /// V-Order enabled, non-partitioned Parquet, and a well-formed struct schema of types a 1/4 table can hold
    /// (<c>timestamp_ntz</c> needs a newer protocol and is rejected) whose field, struct, array and map objects carry
    /// exactly the members the Delta schema serialization defines (a field's <c>metadata</c> included, nothing else).
    /// The message never contains log contents.
    /// </exception>
    /// <exception cref="IOException">The log cannot be read; nothing is retried or substituted.</exception>
    /// <exception cref="UnauthorizedAccessException">The log location is not readable.</exception>
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

        var logBytes = await ReadLogAsync(root, resourceType, cancellationToken);
        var (protocol, metaData) = DeltaLogActions.Read(logBytes);

        var (reader, writer) = ReadProtocol(protocol);
        var schemaString = ReadMetaData(metaData, out var configuration);
        var fields = DeltaSchemaReader.ReadFields(schemaString);

        return new FabricSilverContract(resourceType, schemaString, fields, reader, writer, configuration);
    }

    private static async Task<byte[]> ReadLogAsync(string root, string resourceType, CancellationToken cancellationToken)
    {
        var silverRootLog = Path.Combine(root, resourceType, "_delta_log", LogFileName);
        var packageLog = Path.Combine([root, .. PackageSilverRoot, resourceType, "_delta_log", LogFileName]);

        return await ReadIfPresentAsync(silverRootLog, cancellationToken)
            ?? await ReadIfPresentAsync(packageLog, cancellationToken)
            ?? throw new FileNotFoundException(
                "No Silver creation log (version 0) for the requested resource type was found under the supplied root, " +
                "in either the Silver root layout or the package layout.",
                silverRootLog);
    }

    // Only a missing file or directory means "this layout is not here". Opening the file directly, rather than
    // testing for it first, means access and I/O failures surface instead of being mistaken for absence.
    private static async Task<byte[]?> ReadIfPresentAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static (int Reader, int Writer) ReadProtocol(JsonElement protocol)
    {
        if (protocol.ValueKind != JsonValueKind.Object)
        {
            throw ContractError.Because("the protocol action is not an object.");
        }

        // Table features arrive as readerFeatures/writerFeatures, which only exist from reader 3 / writer 7.
        if (protocol.EnumerateObject().Any(property => !ProtocolProperties.Contains(property.Name, StringComparer.Ordinal)))
        {
            throw ContractError.Because("the protocol declares more than minReaderVersion and minWriterVersion, such as table features.");
        }

        var reader = RequiredInt32(protocol, "minReaderVersion");
        var writer = RequiredInt32(protocol, "minWriterVersion");
        return (reader, writer) == (ExpectedReaderVersion, ExpectedWriterVersion)
            ? (reader, writer)
            : throw ContractError.Because(
                $"the protocol is not reader {ExpectedReaderVersion} / writer {ExpectedWriterVersion}.");
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
        var caseInsensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in node.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw ContractError.Because("a configuration value is not a string.");
            }

            // Property names are already unique (see StrictJson); Delta also treats keys case-insensitively.
            if (!caseInsensitiveNames.Add(property.Name))
            {
                throw ContractError.Because("the configuration repeats a property under different casing.");
            }

            if (UnsupportedConfigurationPrefixes.Any(prefix => property.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                throw ContractError.Because("the configuration enables a table feature that needs more than protocol 1/4.");
            }

            configuration.Add(property.Name, property.Value.GetString()!);
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
