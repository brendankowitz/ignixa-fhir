// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// The JSON form of every response: the MCP server's own serializer settings (camelCase, compact) except that nulls are
/// kept, so every response has the same shape (<c>facts</c>, <c>derived</c> and both <c>freshness</c> members are always present).
/// Used by the MCP tools and the CLI alike so that a response measures the same size on both.
/// </summary>
public static class EnvelopeJson
{
    /// <summary>The hard cap on a serialized MCP response (50 KB, see tool-design.md).</summary>
    public const int MaxBytes = 50 * 1024;

    /// <summary>Compact output.</summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    /// <summary>Indented output for reading by a person.</summary>
    public static JsonSerializerOptions Pretty { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new(McpJsonUtilities.DefaultOptions)
    {
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The UTF-8 size of <paramref name="value"/> serialized compactly.</summary>
    public static int SizeOf<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Compact).Length;
}
