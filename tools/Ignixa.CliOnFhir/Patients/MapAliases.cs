// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// The short names one patient map writes its code systems by: the well-known aliases (<see cref="CodeAliases.Of"/>), and for
/// any other http(s) system the last segment of its URL when that segment is a plain name no other system of the map shares
/// (<c>http://mimic.mit.edu/fhir/mimic/CodeSystem/mimic-d-labitems</c> is <c>mimic-d-labitems</c>). Derived from the systems
/// the map shows, so the same data always gets the same aliases; the map's <c>codeSystems</c> spells each one out.
/// </summary>
internal sealed partial class MapAliases
{
    private const int MaxDerivedLength = 40;

    private readonly Dictionary<string, string> _derived;

    public MapAliases(IEnumerable<string> systems)
    {
        HashSet<string> known = [.. systems.Select(CodeAliases.Of).Where(alias => !IsUrl(alias))];
        List<(string System, string Segment)> candidates = [.. systems
            .Distinct(StringComparer.Ordinal)
            .Where(system => CodeAliases.Of(system) == system && IsUrl(system))
            .Select(system => (System: system, Segment: system[(system.LastIndexOf('/') + 1)..]))
            .Where(candidate => candidate.Segment.Length <= MaxDerivedLength && PlainName().IsMatch(candidate.Segment) && !known.Contains(candidate.Segment))];
        _derived = candidates
            .GroupBy(candidate => candidate.Segment, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Single().System, group => group.Key, StringComparer.Ordinal);
    }

    /// <summary>The alias of <paramref name="system"/> in this map; the system itself, or its digest, when it has none.</summary>
    public string Of(string system) => _derived.TryGetValue(system, out string? alias) ? alias : CodeAliases.Of(system);

    /// <summary>
    /// <paramref name="systems"/>, in their order, as <c>alias=system</c> (or the system itself when it has no alias). Derived
    /// aliases that share a URL prefix are spelled once, at the place of the first, as <c>prefix{alias}: alias, alias</c>.
    /// </summary>
    public IReadOnlyList<string> Spell(IReadOnlyList<string> systems)
    {
        Dictionary<string, List<string>> byPrefix = systems
            .Where(_derived.ContainsKey)
            .GroupBy(Prefix, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Select(system => _derived[system]).ToList(), StringComparer.Ordinal);
        List<string> spelled = [];
        HashSet<string> prefixesSpelled = new(StringComparer.Ordinal);
        foreach (string system in systems)
        {
            string alias = Of(system);
            if (_derived.ContainsKey(system) && byPrefix.TryGetValue(Prefix(system), out List<string>? shared))
            {
                if (prefixesSpelled.Add(Prefix(system)))
                {
                    spelled.Add($"{Prefix(system)}{{alias}}: {string.Join(", ", shared)}");
                }
            }
            else
            {
                spelled.Add(alias != system && !FhirIds.IsDigest(alias) ? $"{alias}={system}" : alias);
            }
        }

        return spelled;
    }

    /// <summary>A <c>system|code</c> key with its system aliased.</summary>
    public string Shorten(string key)
    {
        int bar = key.IndexOf('|', StringComparison.Ordinal);
        return bar < 0 ? key : Of(key[..bar]) + key[bar..];
    }

    private string Prefix(string system) => system[..^_derived[system].Length];

    private static bool IsUrl(string system) =>
        system.StartsWith("http://", StringComparison.Ordinal) || system.StartsWith("https://", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainName();
}
