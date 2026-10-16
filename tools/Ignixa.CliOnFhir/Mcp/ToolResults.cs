// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using ModelContextProtocol.Protocol;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>Packs envelopes into MCP tool results.</summary>
internal static class ToolResults
{
    /// <summary>
    /// The envelope as the tool's text content. A call that could not be answered (null facts) is flagged
    /// <c>isError</c> so the client treats it as a failed call, and still carries the envelope with the issue that explains it.
    /// </summary>
    public static CallToolResult From<TFacts>(ToolEnvelope<TFacts> envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(envelope, EnvelopeJson.Compact) }],
            IsError = envelope.Facts is null,
        };
    }

    /// <summary>
    /// <see cref="From{TFacts}"/> without the members that carry nothing: <c>derived</c> (always null), <c>absent</c> when empty and
    /// <c>freshness</c> when no file changed since the index was built. A call that could not be answered, or whose folder is
    /// stale or unreadable, keeps its freshness.
    /// </summary>
    public static CallToolResult Lean<TFacts>(ToolEnvelope<TFacts> envelope) => new()
    {
        Content = [new TextContentBlock { Text = LeanJson(envelope) }],
        IsError = envelope.Facts is null,
    };

    /// <summary>The JSON <see cref="Lean{TFacts}"/> puts in its text content.</summary>
    public static string LeanJson<TFacts>(ToolEnvelope<TFacts> envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        JsonObject node = JsonSerializer.SerializeToNode(envelope, EnvelopeJson.Compact)!.AsObject();
        node.Remove("derived");
        if (envelope.Absent.Count == 0)
        {
            node.Remove("absent");
        }

        if (envelope.Freshness.StaleFiles == 0)
        {
            node.Remove("freshness");
        }

        return node.ToJsonString(EnvelopeJson.Compact);
    }
}
