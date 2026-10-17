// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>One code's use while <see cref="FolderCodeText"/> reads the indexes.</summary>
internal sealed class CodeTally
{
    public string? Display { get; private set; }

    public Dictionary<string, int> Types { get; } = new(StringComparer.Ordinal);

    public int Resources { get; private set; }

    public int Active { get; private set; }

    public void Add(string resourceType, string? display, bool active)
    {
        Display ??= string.IsNullOrWhiteSpace(display) ? null : display;
        Types[resourceType] = Types.GetValueOrDefault(resourceType) + 1;
        Resources++;
        Active += active ? 1 : 0;
    }
}
