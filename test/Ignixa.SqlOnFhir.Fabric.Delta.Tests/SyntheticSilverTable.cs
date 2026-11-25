// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Invented Silver contracts and Delta logs. Nothing here is derived from a real Healthcare data solutions package;
/// the schema only exercises nested struct, array, decimal and non-ASCII metadata shapes.
/// </summary>
internal static class SyntheticSilverTable
{
    public const string ChangeDataFeed = "delta.enableChangeDataFeed";
    public const string VOrder = "delta.parquet.vorder.enabled";
    public const string LogRetention = "delta.logRetentionDuration";

    private static readonly JsonSerializerOptions RawText = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Hand-formatted so exact-string preservation is observable (line breaks, spacing and key order are deliberate).</summary>
    public static string SchemaString() =>
        """
        {"type":"struct","fields":[
        {"name":"id","type":"string","nullable":false,"metadata":{"comment":"Größe – identifier"}},
        {"name":"meta","type":{"type":"struct","fields":[{"name":"versionId","type":"string","nullable":true,"metadata":{}},{"name":"lastUpdated","type":"timestamp","nullable":true,"metadata":{}}]},"nullable":true,"metadata":{}},
        {"name":"scores","type":{"type":"array","elementType":"decimal(18,6)","containsNull":true},"nullable":true,"metadata":{}},
        {"name":"born",  "type":"date","nullable":true,"metadata":{}}
        ]}
        """;

    /// <summary>The creation template configuration, with the change feed spelled <c>True</c> as the HDS package does.</summary>
    public static JsonObject TemplateConfiguration() => new()
    {
        [ChangeDataFeed] = "True",
        [VOrder] = "true",
        [LogRetention] = "interval 30 days",
    };

    /// <summary>The configuration a correctly created table holds: the template with the change feed lowercased.</summary>
    public static JsonObject TableConfiguration()
    {
        var configuration = TemplateConfiguration();
        configuration[ChangeDataFeed] = "true";
        return configuration;
    }

    /// <summary>
    /// Loads a contract from an invented creation template, by default with <see cref="SchemaString"/> and
    /// <see cref="TemplateConfiguration"/>.
    /// </summary>
    public static async Task<FabricSilverContract> LoadContractAsync(
        JsonObject? templateConfiguration = null,
        string? schemaString = null,
        string resourceType = "Patient")
    {
        using var root = new TemporaryDirectory();
        WriteCommit(
            root.Combine(resourceType),
            0,
            CommitInfoLine(),
            ProtocolLine(),
            MetaDataLine(schemaString, configuration: templateConfiguration ?? TemplateConfiguration()));
        return await FabricSilverContract.LoadAsync(root.Path, resourceType, CancellationToken.None);
    }

    public static string CommitInfoLine() => """{"commitInfo":{"timestamp":1700000000000,"operation":"CREATE TABLE"}}""";

    public static string ProtocolLine(int reader = 1, int writer = 4) =>
        $$$"""{"protocol":{"minReaderVersion":{{{reader}}},"minWriterVersion":{{{writer}}}}}""";

    /// <summary>A metaData line that matches the synthetic contract unless an argument overrides a part of it.</summary>
    public static string MetaDataLine(
        string? schemaString = null,
        JsonNode? partitionColumns = null,
        JsonObject? configuration = null)
    {
        var metaData = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("D"),
            ["format"] = new JsonObject { ["provider"] = "parquet", ["options"] = new JsonObject() },
            ["schemaString"] = schemaString ?? SchemaString(),
            ["partitionColumns"] = partitionColumns ?? new JsonArray(),
            ["configuration"] = configuration ?? TableConfiguration(),
            ["createdTime"] = 1700000000000,
        };
        return new JsonObject { ["metaData"] = metaData }.ToJsonString(RawText);
    }

    /// <summary>A table configuration with one property set (or removed, when <paramref name="value"/> is null).</summary>
    public static JsonObject TableConfigurationWith(string name, string? value)
    {
        var configuration = TableConfiguration();
        if (value is null)
        {
            configuration.Remove(name);
        }
        else
        {
            configuration[name] = value;
        }

        return configuration;
    }

    /// <summary>Writes a commit as Delta does: UTF-8 without a byte order mark, one action per <c>\n</c>-terminated line.</summary>
    public static void WriteCommit(string tableDirectory, long version, params string[] lines)
    {
        var logDirectory = Path.Combine(tableDirectory, "_delta_log");
        Directory.CreateDirectory(logDirectory);
        File.WriteAllText(
            Path.Combine(logDirectory, $"{version:D20}.json"),
            string.Concat(lines.Select(line => line + "\n")),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Writes a version 0 another writer could have created, matching the contract unless overridden.</summary>
    public static void WriteExistingTable(string tableDirectory, string? metaDataLine = null, string? protocolLine = null) =>
        WriteCommit(tableDirectory, 0, CommitInfoLine(), protocolLine ?? ProtocolLine(), metaDataLine ?? MetaDataLine());
}
