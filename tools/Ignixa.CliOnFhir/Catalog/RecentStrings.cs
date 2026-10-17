// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// The last string read in each of a few slots, handed back while the next value's UTF-8 is the same, so cataloguing a file of
/// one kind reads its profile, identifier system and codes once instead of once per line. Not thread-safe.
/// </summary>
public sealed class RecentStrings
{
    public const int Profile = 0;
    public const int System = 1;
    public const int Code = 2;

    private readonly byte[]?[] _bytes = new byte[]?[3];
    private readonly string?[] _strings = new string?[3];

    /// <summary>The string the reader is on (a JSON string token): the slot's previous instance when the text is the same.</summary>
    public string? Read(ref Utf8JsonReader reader, int slot)
    {
        if (!reader.ValueIsEscaped && !reader.HasValueSequence && _bytes[slot] is { } last && reader.ValueSpan.SequenceEqual(last))
        {
            return _strings[slot];
        }

        string? value = reader.GetString();
        _bytes[slot] = reader.ValueIsEscaped || reader.HasValueSequence ? null : reader.ValueSpan.ToArray();
        _strings[slot] = value;
        return value;
    }
}
