// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>One system's counts while the walk runs.</summary>
internal sealed class SystemAccumulator
{
    public Dictionary<string, int> Codes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> Elements { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Resources { get; } = new(StringComparer.Ordinal);

    public int Codings { get; private set; }

    public void Add(string resource, string element, string code)
    {
        Codes[code] = Codes.GetValueOrDefault(code) + 1;
        Elements[element] = Elements.GetValueOrDefault(element) + 1;
        Resources.Add(resource);
        Codings++;
    }
}
