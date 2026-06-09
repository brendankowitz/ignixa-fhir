// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Exposes <see cref="ExtractedResource"/> ValueSets and CodeSystems from a FHIR IG package
/// as an <see cref="IValueSetProvider"/> consumable by <c>InMemoryTerminologyService</c>.
/// <para>
/// Supports two ValueSet shapes:
/// </para>
/// <list type="number">
///   <item>Inline concepts: <c>compose.include[].concept[]</c> lists codes directly.</item>
///   <item>CodeSystem reference: <c>compose.include[].system</c> (no concepts) means
///         "all codes from the referenced CodeSystem". The matching <c>CodeSystem</c>
///         must also be in the supplied resources for expansion to succeed.</item>
/// </list>
/// <para>
/// Out of scope (treated as unknown for now): <c>compose.include[].valueSet</c>
/// chaining, <c>compose.exclude</c>, intensional <c>compose.include[].filter</c>,
/// and pre-computed <c>expansion.contains</c>. A future enhancement can resolve these
/// against a wider package set.
/// </para>
/// </summary>
public sealed class PackageValueSetSource : IValueSetProvider
{
    private readonly Dictionary<string, ExtractedResource> _valueSets;
    private readonly Dictionary<string, ExtractedResource> _codeSystems;
    private readonly ConcurrentDictionary<string, IReadOnlyList<FhirCode>> _expansionCache = new(StringComparer.Ordinal);

    public PackageValueSetSource(IEnumerable<ExtractedResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _valueSets = new(StringComparer.Ordinal);
        _codeSystems = new(StringComparer.Ordinal);
        foreach (var r in resources)
        {
            switch (r.ResourceType)
            {
                case "ValueSet":
                    _valueSets[r.Canonical] = r;
                    break;
                case "CodeSystem":
                    _codeSystems[r.Canonical] = r;
                    break;
            }
        }
    }

    public bool IsKnownValueSet(string valueSetUrl)
        => GetCodes(valueSetUrl) != null;

    public bool? IsValidCode(string valueSetUrl, string code)
    {
        var codes = GetCodes(valueSetUrl);
        if (codes == null)
        {
            return null;
        }
        foreach (var c in codes)
        {
            if (string.Equals(c.Code, code, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public IReadOnlyList<FhirCode>? GetCodes(string valueSetUrl)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return null;
        }

        var canonical = StripVersionSuffix(valueSetUrl);

        if (_expansionCache.TryGetValue(canonical, out var cached))
        {
            return cached;
        }

        if (!_valueSets.TryGetValue(canonical, out var vs))
        {
            return null;
        }

        var expanded = ExpandValueSet(vs);
        if (expanded != null)
        {
            _expansionCache[canonical] = expanded;
        }
        return expanded;
    }

    private IReadOnlyList<FhirCode>? ExpandValueSet(ExtractedResource valueSet)
    {
        try
        {
            using var doc = JsonDocument.Parse(valueSet.ResourceJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("compose", out var compose) ||
                compose.ValueKind != JsonValueKind.Object ||
                !compose.TryGetProperty("include", out var includes) ||
                includes.ValueKind != JsonValueKind.Array)
            {
                // No expandable compose.include (e.g. a pre-expanded expansion.contains-only
                // ValueSet). We cannot enumerate the exact member set, so report "unable to
                // expand" (null) rather than an empty set — an empty set would cause a required
                // binding to reject every otherwise-valid code (false positives).
                return null;
            }

            // compose.exclude makes an include-only expansion an over-approximation; we cannot
            // soundly decide membership, so treat the whole ValueSet as unexpandable.
            if (compose.TryGetProperty("exclude", out _))
            {
                return null;
            }

            var codes = new List<FhirCode>();
            foreach (var include in includes.EnumerateArray())
            {
                var system = include.TryGetProperty("system", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null;

                if (include.TryGetProperty("concept", out var conceptArr) && conceptArr.ValueKind == JsonValueKind.Array)
                {
                    // Inline concepts: directly enumerable.
                    foreach (var concept in conceptArr.EnumerateArray())
                    {
                        var code = concept.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        var display = concept.TryGetProperty("display", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                        if (!string.IsNullOrEmpty(code))
                        {
                            codes.Add(new FhirCode(system ?? string.Empty, code!, display ?? string.Empty));
                        }
                    }
                    continue;
                }

                // No inline concepts: this include enumerates codes by reference. Intensional
                // filters and nested ValueSet chains cannot be enumerated here, and a
                // whole-CodeSystem inclusion requires the referenced CodeSystem to be present in
                // the package. Any of these means we cannot produce the exact member set, so
                // return null (binding degrades to a warning) instead of partially expanding and
                // rejecting valid codes that come from the unexpandable include.
                if (string.IsNullOrEmpty(system) ||
                    include.TryGetProperty("filter", out _) ||
                    include.TryGetProperty("valueSet", out _))
                {
                    return null;
                }

                var fromCodeSystem = ExpandCodeSystem(system!);
                if (fromCodeSystem == null || fromCodeSystem.Count == 0)
                {
                    // CodeSystem not in package, or present but not enumerable (e.g. content
                    // other than "complete") — cannot assert the full set of valid codes.
                    return null;
                }
                codes.AddRange(fromCodeSystem);
            }

            // If nothing enumerable was found, treat as unable-to-expand rather than an empty
            // (reject-everything) set.
            return codes.Count > 0 ? codes : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IReadOnlyList<FhirCode>? ExpandCodeSystem(string systemUrl)
    {
        if (!_codeSystems.TryGetValue(systemUrl, out var cs))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(cs.ResourceJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("concept", out var conceptArr) || conceptArr.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<FhirCode>();
            }

            var codes = new List<FhirCode>();
            CollectConcepts(conceptArr, systemUrl, codes);
            return codes;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CollectConcepts(JsonElement conceptArr, string systemUrl, List<FhirCode> codes)
    {
        foreach (var concept in conceptArr.EnumerateArray())
        {
            var code = concept.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var display = concept.TryGetProperty("display", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            if (!string.IsNullOrEmpty(code))
            {
                codes.Add(new FhirCode(systemUrl, code!, display ?? string.Empty));
            }
            // Hierarchical CodeSystems use nested concept[] - recurse.
            if (concept.TryGetProperty("concept", out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                CollectConcepts(nested, systemUrl, codes);
            }
        }
    }

    private static string StripVersionSuffix(string url)
    {
        var pipe = url.IndexOf('|', StringComparison.Ordinal);
        return pipe >= 0 ? url[..pipe] : url;
    }
}
