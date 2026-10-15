// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>Accumulates codings during the walk; <see cref="Build"/> turns them into the immutable, sorted inventory.</summary>
internal sealed class InventoryBuilder
{
    private readonly Dictionary<string, SystemAccumulator> _systems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _implicit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _withoutSystem = new(StringComparer.Ordinal);

    public void Coding(string resource, string element, string? system, string code)
    {
        if (system is null)
        {
            _withoutSystem[element] = _withoutSystem.GetValueOrDefault(element) + 1;
            return;
        }

        if (!_systems.TryGetValue(system, out SystemAccumulator? accumulator))
        {
            accumulator = new SystemAccumulator();
            _systems[system] = accumulator;
        }

        accumulator.Add(resource, element, code);
    }

    public void Implicit(string element, string code)
    {
        if (!_implicit.TryGetValue(element, out Dictionary<string, int>? codes))
        {
            codes = new Dictionary<string, int>(StringComparer.Ordinal);
            _implicit[element] = codes;
        }

        codes[code] = codes.GetValueOrDefault(code) + 1;
    }

    public (IReadOnlyList<SystemTally> Systems, IReadOnlyList<ImplicitTally> Implicit, IReadOnlyList<ElementCount> WithoutSystem) Build() =>
    (
        [.. _systems
            .Select(pair => new SystemTally(pair.Key, Sorted(pair.Value.Codes), pair.Value.Codings, pair.Value.Resources.Count, Sorted(pair.Value.Elements).Select(element => new ElementCount(element.Code, element.Count)).ToList()))
            .OrderByDescending(tally => tally.Codings)
            .ThenBy(tally => tally.System, StringComparer.Ordinal)],
        [.. _implicit
            .Select(pair => new ImplicitTally(pair.Key, Sorted(pair.Value), pair.Value.Values.Sum()))
            .OrderByDescending(tally => tally.Occurrences)
            .ThenBy(tally => tally.Element, StringComparer.Ordinal)],
        [.. Sorted(_withoutSystem).Select(element => new ElementCount(element.Code, element.Count))]
    );

    private static List<(string Code, int Count)> Sorted(Dictionary<string, int> counts) =>
        [.. counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value))];
}
