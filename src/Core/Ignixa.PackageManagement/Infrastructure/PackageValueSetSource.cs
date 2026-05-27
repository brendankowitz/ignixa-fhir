// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
    private readonly Dictionary<string, IReadOnlyList<FhirCode>> _expansionCache = new(StringComparer.Ordinal);

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
            if (!root.TryGetProperty("compose", out var compose) ||
                !compose.TryGetProperty("include", out var includes) ||
                includes.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<FhirCode>();
            }

            var codes = new List<FhirCode>();
            foreach (var include in includes.EnumerateArray())
            {
                var system = include.TryGetProperty("system", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null;

                if (include.TryGetProperty("concept", out var conceptArr) && conceptArr.ValueKind == JsonValueKind.Array)
                {
                    // Inline concepts
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

                if (!string.IsNullOrEmpty(system) &&
                    !include.TryGetProperty("filter", out _) &&
                    !include.TryGetProperty("valueSet", out _))
                {
                    // Whole-CodeSystem inclusion
                    var fromCodeSystem = ExpandCodeSystem(system!);
                    if (fromCodeSystem != null)
                    {
                        codes.AddRange(fromCodeSystem);
                    }
                }
                // Unsupported shapes (filter, valueSet chain, exclude) are silently skipped.
            }

            return codes;
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
