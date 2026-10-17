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

        Console.Out.WriteLine(JsonSerializer.Serialize(envelope, pretty ? EnvelopeJson.Pretty : EnvelopeJson.Compact));
        return envelope.Facts is null ? 1 : 0;
    }
}
