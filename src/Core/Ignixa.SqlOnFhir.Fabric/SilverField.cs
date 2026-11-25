// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// One field of a Silver struct (or one top-level column).
/// </summary>
/// <remarks>
/// <see cref="OriginalOf"/> carries the Silver naming convention for originals: a field <c>xOrig</c> next to a sibling
/// <c>x</c> holds the source value of <c>x</c> as it was before normalization, read into the original's own type
/// (for example <c>birthDateOrig</c> is a date while <c>birthDate</c> is a timestamp).
/// </remarks>
internal sealed class SilverField
{
    public SilverField(string name, SilverType type, bool nullable, string? originalOf)
    {
        Name = name;
        Type = type;
        Nullable = nullable;
        OriginalOf = originalOf;
    }

    public string Name { get; }

    public SilverType Type { get; }

    public bool Nullable { get; }

    /// <summary>The sibling whose source value this field preserves, or null when the field reads its own name.</summary>
    public string? OriginalOf { get; }

    /// <summary>The source property this field is read from outside a normalized reference.</summary>
    public string SourceName => OriginalOf ?? Name;
}
