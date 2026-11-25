// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>
/// A temporary directory holding invented creation logs. Nothing here is derived from a real
/// Healthcare data solutions package; the schemas only exercise nested struct/list/decimal shapes.
/// </summary>
internal sealed class SyntheticSilverRoot : IDisposable
{
    public const string LogFileName = "00000000000000000000.json";

    private static readonly string[] PackageSilverSegments =
        ["HDS.SourceCode", "hds-build-artifacts", "healthcare-tables", "1.4.0", "Silver"];

    /// <summary>Serializes non-ASCII text as itself, so files hold real multi-byte UTF-8 rather than escapes.</summary>
    public static readonly JsonSerializerOptions RawText = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public SyntheticSilverRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ignixa-fabric-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Hand-formatted so that exact-string preservation is observable (spacing and key order are deliberate).</summary>
    public static string SchemaString() =>
        """
        {"type":"struct","fields":[
        {"name":"rowKey","type":"string","nullable":false,"metadata":{}},
        {"name":"tags","type":{"type":"array","elementType":{"type":"struct","fields":[{"name":"label","type":"string","nullable":true,"metadata":{}},{"name":"weights","type":{"type":"array","elementType":"decimal(18,6)","containsNull":true},"nullable":true,"metadata":{}}]},"containsNull":true},"nullable":true,"metadata":{}},
        {"name":"detail","type":{"type":"struct","fields":[{"name":"amount","type":"decimal(38,10)","nullable":true,"metadata":{}},{"name":"inner","type":{"type":"struct","fields":[{"name":"flag","type":"boolean","nullable":true,"metadata":{}},{"name":"at","type":"timestamp","nullable":true,"metadata":{}}]},"nullable":true,"metadata":{}}]},"nullable":true,"metadata":{}},
        {"name":"lookup","type":{"type":"map","keyType":"string","valueType":"long","valueContainsNull":true},"nullable":true,"metadata":{}},
        {"name":"seenOn","type":"date","nullable":true,"metadata":{}}
        ]}
        """;

    public static string ProtocolLine(int reader = 1, int writer = 4) =>
        $$$"""{"protocol":{"minReaderVersion":{{{reader}}},"minWriterVersion":{{{writer}}}}}""";

    public static string MetaDataLine(
        string? schemaString = null,
        JsonNode? format = null,
        JsonNode? partitionColumns = null,
        JsonNode? configuration = null)
    {
        var metaData = new JsonObject
        {
            ["id"] = "00000000-0000-0000-0000-000000000000",
            ["format"] = format ?? new JsonObject { ["provider"] = "parquet", ["options"] = new JsonObject() },
            ["schemaString"] = schemaString ?? SchemaString(),
            ["partitionColumns"] = partitionColumns ?? new JsonArray(),
            ["configuration"] = configuration ?? ValidConfiguration(),
        };
        return new JsonObject { ["metaData"] = metaData }.ToJsonString(RawText);
    }

    public static JsonObject ValidConfiguration() => new()
    {
        ["delta.enableChangeDataFeed"] = "True",
        ["delta.parquet.vorder.enabled"] = "true",
    };

    public static string CommitInfoLine() => """{"commitInfo":{"operation":"CREATE TABLE"}}""";

    /// <summary>Metadata line whose raw JSON is rewritten by <paramref name="mutate"/> (to drop or alter a property).</summary>
    public static string MetaDataLineWith(Action<JsonObject> mutate)
    {
        var line = (JsonObject)JsonNode.Parse(MetaDataLine())!;
        mutate((JsonObject)line["metaData"]!);
        return line.ToJsonString();
    }

    public static string SchemaLine(JsonNode schema) => MetaDataLine(schemaString: schema.ToJsonString());

    public static JsonObject SchemaOf(params JsonNode[] fields) => new()
    {
        ["type"] = "struct",
        ["fields"] = new JsonArray(fields),
    };

    public static JsonObject Field(string name, JsonNode type, bool nullable = true) => new()
    {
        ["name"] = name,
        ["type"] = type,
        ["nullable"] = nullable,
        ["metadata"] = new JsonObject(),
    };

    public string WriteSilverLog(string resourceType, params string[] lines) =>
        WriteLog(System.IO.Path.Combine(Path, resourceType), lines);

    public string WritePackageLog(string resourceType, params string[] lines) =>
        WriteLog(System.IO.Path.Combine([Path, .. PackageSilverSegments, resourceType]), lines);

    /// <summary>Writes the version 0 log as raw bytes, for encodings and malformed input that text APIs would repair.</summary>
    public string WriteSilverLogBytes(string resourceType, byte[] content)
    {
        var logPath = System.IO.Path.Combine(Path, resourceType, "_delta_log", LogFileName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
        File.WriteAllBytes(logPath, content);
        return logPath;
    }

    /// <summary>Writes another file (a later commit, a checkpoint) into the table's <c>_delta_log</c>.</summary>
    public void WriteSilverLogFile(string resourceType, string fileName, params string[] lines)
    {
        var logDirectory = System.IO.Path.Combine(Path, resourceType, "_delta_log");
        Directory.CreateDirectory(logDirectory);
        File.WriteAllLines(System.IO.Path.Combine(logDirectory, fileName), lines);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private static string WriteLog(string tableDirectory, string[] lines)
    {
        var logDirectory = System.IO.Path.Combine(tableDirectory, "_delta_log");
        Directory.CreateDirectory(logDirectory);
        var logPath = System.IO.Path.Combine(logDirectory, LogFileName);
        File.WriteAllLines(logPath, lines);
        return logPath;
    }
}
