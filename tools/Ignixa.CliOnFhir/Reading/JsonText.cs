// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>Reads a string from folder JSON whose shape is not trusted: anything that is not a JSON string is absent.</summary>
internal static class JsonText
{
    public static string? Of(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
