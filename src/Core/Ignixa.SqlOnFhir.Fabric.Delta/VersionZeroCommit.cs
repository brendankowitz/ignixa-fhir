// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// The version 0 commit of a Silver table: <c>commitInfo</c>, <c>protocol</c> and <c>metaData</c>, one JSON action per
/// line, UTF-8 without a byte order mark, each line ending in <c>\n</c>.
/// </summary>
internal static class VersionZeroCommit
{
    internal const string FileName = "00000000000000000000.json";
    internal const int MinReaderVersion = 1;
    internal const int MinWriterVersion = 4;
    internal const string ChangeDataFeedProperty = "delta.enableChangeDataFeed";

    // delta-rs enables the change feed only for lowercase "true"; Spark reads the flag case-insensitively, so the
    // lowercase value is the one both engines agree on.
    internal const string ChangeDataFeedEnabled = "true";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Builds the commit for <paramref name="contract"/>: protocol 1/4 without features, the contract's schema string
    /// exactly, no partition columns and the contract configuration in its order with the change feed flag lowercased.
    /// </summary>
    internal static byte[] Create(FabricSilverContract contract, Guid tableId, DateTimeOffset createdAt)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var epochMilliseconds = createdAt.ToUnixTimeMilliseconds();

        WriteLine(buffer, writer => WriteCommitInfo(writer, epochMilliseconds));
        WriteLine(buffer, WriteProtocol);
        WriteLine(buffer, writer => WriteMetaData(writer, contract, tableId, epochMilliseconds));

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteLine(ArrayBufferWriter<byte> buffer, Action<Utf8JsonWriter> writeAction)
    {
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writeAction(writer);
            writer.WriteEndObject();
        }

        buffer.Write("\n"u8);
    }

    private static void WriteCommitInfo(Utf8JsonWriter writer, long epochMilliseconds)
    {
        writer.WriteStartObject("commitInfo");
        writer.WriteNumber("timestamp", epochMilliseconds);
        writer.WriteString("operation", "CREATE TABLE");
        writer.WriteStartObject("operationParameters");
        writer.WriteString("mode", "ErrorIfExists");
        writer.WriteString("partitionBy", "[]");
        writer.WriteEndObject();
        writer.WriteString("isolationLevel", "Serializable");
        writer.WriteBoolean("isBlindAppend", true);
        writer.WriteString("engineInfo", "Ignixa.SqlOnFhir.Fabric.Delta");
        writer.WriteString("txnId", Guid.NewGuid().ToString("D"));
        writer.WriteEndObject();
    }

    private static void WriteProtocol(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("protocol");
        writer.WriteNumber("minReaderVersion", MinReaderVersion);
        writer.WriteNumber("minWriterVersion", MinWriterVersion);
        writer.WriteEndObject();
    }

    private static void WriteMetaData(Utf8JsonWriter writer, FabricSilverContract contract, Guid tableId, long epochMilliseconds)
    {
        writer.WriteStartObject("metaData");
        writer.WriteString("id", tableId.ToString("D"));

        writer.WriteStartObject("format");
        writer.WriteString("provider", "parquet");
        writer.WriteStartObject("options");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteString("schemaString", contract.SchemaString);

        writer.WriteStartArray("partitionColumns");
        writer.WriteEndArray();

        writer.WriteStartObject("configuration");
        foreach (var (name, value) in contract.Configuration)
        {
            writer.WriteString(name, name == ChangeDataFeedProperty ? ChangeDataFeedEnabled : value);
        }

        writer.WriteEndObject();

        writer.WriteNumber("createdTime", epochMilliseconds);
        writer.WriteEndObject();
    }
}
