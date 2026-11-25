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
/// </summary>
internal static class DeltaLogActions
{
    private const string ProtocolAction = "protocol";
    private const string MetaDataAction = "metaData";

    public static async Task<(JsonElement Protocol, JsonElement MetaData)> ReadAsync(
        string logPath,
        CancellationToken cancellationToken)
    {
        JsonElement? protocol = null;
        JsonElement? metaData = null;

        await using var stream = new FileStream(
            logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = ParseLine(line, lineNumber);
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

    private static JsonDocument ParseLine(string line, int lineNumber)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            // The parser's message can quote input characters, so it is deliberately not chained.
            throw ContractError.Because($"line {lineNumber} is not valid JSON.");
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
