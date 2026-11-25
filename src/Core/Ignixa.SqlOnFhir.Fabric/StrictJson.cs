// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Parses JSON text the way the contract needs it: unambiguous and fully decodable. Beyond well-formedness it rejects
/// objects that repeat a property name (a reader could otherwise pick either value) and strings or property names
/// whose escapes do not decode to valid Unicode (a lone surrogate escape parses but cannot be read as text).
/// Failures are <see cref="InvalidDataException"/>s that never quote the input.
/// </summary>
internal static class StrictJson
{
    /// <param name="text">The JSON text.</param>
    /// <param name="subject">What is being parsed, as the start of a failure message (for example "line 3").</param>
    public static JsonDocument Parse(string text, string subject)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            // The parser's message can quote input characters, so it is deliberately not chained.
            throw ContractError.Because($"{subject} is not valid JSON within the supported nesting depth.");
        }

        try
        {
            Validate(document.RootElement, subject);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void Validate(JsonElement element, string subject)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Decode(() => element.GetString(), subject);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Validate(item, subject);
                }

                break;
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(Decode(() => property.Name, subject)))
                    {
                        throw ContractError.Because($"{subject} repeats a property name within an object.");
                    }

                    Validate(property.Value, subject);
                }

                break;
        }
    }

    private static string Decode(Func<string?> read, string subject)
    {
        try
        {
            return read()!;
        }
        catch (InvalidOperationException)
        {
            // Raised for escapes such as a lone surrogate; the message is not chained because it is about input text.
            throw ContractError.Because($"{subject} contains text that is not valid Unicode.");
        }
    }
}
