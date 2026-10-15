// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
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
}
