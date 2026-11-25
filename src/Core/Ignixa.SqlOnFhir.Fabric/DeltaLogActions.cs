// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Reads a Delta creation log (newline-delimited JSON, one action per line) and picks out the
/// <c>protocol</c> and <c>metaData</c> actions by name, so that line positions and unrelated actions do not matter.
/// The bytes must be strict UTF-8 without a byte order mark: no other encoding is detected or accepted and nothing
/// is replaced, so a malformed byte can never silently become a different character.
/// </summary>
internal static class DeltaLogActions
{
    private const string ProtocolAction = "protocol";
    private const string MetaDataAction = "metaData";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    public static (JsonElement Protocol, JsonElement MetaData) Read(ReadOnlySpan<byte> log)
    {
        // Strict decoding already refuses the UTF-16 and UTF-32 marks; only the UTF-8 mark decodes to a character.
        if (log.StartsWith(Utf8ByteOrderMark))
        {
            throw ContractError.Because("the log starts with a byte order mark; it must be UTF-8 without one.");
        }

        var text = Decode(log);

        JsonElement? protocol = null;
        JsonElement? metaData = null;

        using var reader = new StringReader(text);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = StrictJson.Parse(line, $"line {lineNumber}");
            var action = SingleAction(document.RootElement, lineNumber);
            switch (action.Name)
            {
                case ProtocolAction:
                    Claim(ref protocol, action.Value, ProtocolAction);
                    break;
                case MetaDataAction:
                    Claim(ref metaData, action.Value, MetaDataAction);
                    break;
            }
        }

        return (
            protocol ?? throw ContractError.Because($"the {ProtocolAction} action is missing."),
            metaData ?? throw ContractError.Because($"the {MetaDataAction} action is missing."));
    }

    private static string Decode(ReadOnlySpan<byte> log)
    {
        try
        {
            return StrictUtf8.GetString(log);
        }
        catch (DecoderFallbackException)
        {
            // The exception names the offending bytes, so it is deliberately not chained.
            throw ContractError.Because("the log is not valid UTF-8.");
        }
    }
    private static JsonProperty SingleAction(JsonElement line, int lineNumber)
    {
        if (line.ValueKind != JsonValueKind.Object)
        {
            throw ContractError.Because($"line {lineNumber} is not an action object.");
        }

        var properties = line.EnumerateObject();
        if (!properties.MoveNext())
        {
            throw ContractError.Because($"line {lineNumber} is an empty object, not an action.");
        }

        var action = properties.Current;
        if (properties.MoveNext())
        {
            throw ContractError.Because($"line {lineNumber} carries more than one action.");
        }

        return action;
    }

    private static void Claim(ref JsonElement? slot, JsonElement value, string actionName)
    {
        if (slot is not null)
        {
            throw ContractError.Because($"the {actionName} action appears more than once.");
        }

        slot = value.Clone();
    }
}
