// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Validation.Services.Terminology;

/// <summary>
/// System-aware membership snapshot of a locally-expanded ValueSet. Tracks which (system, code)
/// pairs the expansion enumerates, the bare code strings (for systemless <c>code</c>-typed bindings),
/// and the set of systems the expansion covers so that a code carrying an unenumerated system can be
/// reported as unverifiable rather than absent.
/// </summary>
internal sealed class ValueSetMembership
{
    private readonly HashSet<(string System, string Code)> _pairs = new();
    private readonly HashSet<string> _codes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _systems = new(StringComparer.Ordinal);

    public ValueSetMembership(IReadOnlyList<FhirCode> codes)
    {
        foreach (var entry in codes)
        {
            // FhirCode is a non-nullable readonly record struct, so entry/entry.Code can't be
            // null; guard only against an empty code (a malformed expansion entry).
            if (string.IsNullOrEmpty(entry.Code))
            {
                continue;
            }

            _codes.Add(entry.Code);
            if (!string.IsNullOrWhiteSpace(entry.System))
            {
                _systems.Add(entry.System);
                _pairs.Add((entry.System, entry.Code));
            }
        }
    }

    public bool ContainsPair(string system, string code) => _pairs.Contains((system, code));

    public bool ContainsCode(string code) => _codes.Contains(code);

    public bool EnumeratesSystem(string system) => _systems.Contains(system);
}
