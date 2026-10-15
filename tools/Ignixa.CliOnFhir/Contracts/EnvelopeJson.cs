// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using ModelContextProtocol;

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// The JSON form of every response: the MCP server's own serializer settings (camelCase, nulls omitted, compact), used by
/// the MCP tools and the CLI alike so that a response measures the same size on both.
/// </summary>
public static class EnvelopeJson
{
    /// <summary>The hard cap on a serialized MCP response (50 KB, see tool-design.md).</summary>
    public const int MaxBytes = 50 * 1024;

    /// <summary>Compact output.</summary>
    public static JsonSerializerOptions Compact { get; } = new(McpJsonUtilities.DefaultOptions) { WriteIndented = false };

    /// <summary>Indented output for reading by a person.</summary>
    public static JsonSerializerOptions Pretty { get; } = new(McpJsonUtilities.DefaultOptions) { WriteIndented = true };

    /// <summary>The UTF-8 size of <paramref name="value"/> serialized compactly.</summary>
    public static int SizeOf<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Compact).Length;
}
