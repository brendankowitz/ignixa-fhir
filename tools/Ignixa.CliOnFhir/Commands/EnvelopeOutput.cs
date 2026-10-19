// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>Writes a tool envelope to stdout as indented JSON.</summary>
internal static class EnvelopeOutput
{
    /// <summary>
    /// Prints <paramref name="envelope"/> as compact JSON (indented when <paramref name="pretty"/>) and returns the exit
    /// code: 0, or 1 when the request failed (no facts).
    /// </summary>
    public static int Write<TFacts>(ToolEnvelope<TFacts> envelope, bool pretty = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // The last line of defence of the hard cap: an answer every fit missed is withheld, as over MCP (ResponseCeiling).
        int bytes = EnvelopeJson.SizeOf(envelope);
        if (bytes > EnvelopeJson.MaxBytes)
        {
            Console.Error.WriteLine($"{ResponseCeiling.Code}: the answer was {bytes} bytes, over the {EnvelopeJson.MaxBytes}-byte response ceiling, and was withheld.");
            var withheld = new ToolEnvelope<object?>(null, [], [], [], [ResponseCeiling.Issue("the command", bytes, EnvelopeJson.MaxBytes)], envelope.Freshness) { AsOf = envelope.AsOf };
            Console.Out.WriteLine(JsonSerializer.Serialize(withheld, pretty ? EnvelopeJson.Pretty : EnvelopeJson.Compact));
            return 1;
        }

        Console.Out.WriteLine(JsonSerializer.Serialize(envelope, pretty ? EnvelopeJson.Pretty : EnvelopeJson.Compact));
        return envelope.Facts is null ? 1 : 0;
    }
}
