// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// Writes a response's evidence as <c>{"files":[...],"items":[{"reference","file":index into files,"line"?,"offset"}]}</c>
/// so that a file shared by many resources (one patient's Bundle) is named once, not once per resource. <c>line</c> is left
/// out when the resource is not on a line of an NDJSON file. Write-only: responses are never read back.
/// </summary>
public sealed class EvidenceJsonConverter : JsonConverter<IReadOnlyList<EvidenceItem>>
{
    public override IReadOnlyList<EvidenceItem> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Evidence is written in a compact form that is not read back.");

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<EvidenceItem> value, JsonSerializerOptions options)
    {
        Dictionary<string, int> indexes = new(StringComparer.Ordinal);
        foreach (EvidenceItem item in value)
        {
            indexes.TryAdd(item.File, indexes.Count);
        }

        writer.WriteStartObject();
        writer.WriteStartArray("files");
        foreach (string file in indexes.Keys)
        {
            writer.WriteStringValue(file);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("items");
        foreach (EvidenceItem item in value)
        {
            writer.WriteStartObject();
            writer.WriteString("reference", item.Reference);
            writer.WriteNumber("file", indexes[item.File]);
            if (item.Line is int line)
            {
                writer.WriteNumber("line", line);
            }

            writer.WriteNumber("offset", item.Offset);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
