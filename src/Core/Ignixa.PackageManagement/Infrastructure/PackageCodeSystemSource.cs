// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Exposes <see cref="ExtractedResource"/> <c>CodeSystem</c> concept content from loaded FHIR
/// packages as an <see cref="ICodeSystemProvider"/>. This is the code&#8594;display and membership
/// resolution surface a terminology service consults for <c>$lookup</c>-style queries; it does not
/// perform binding validation.
/// <para>
/// Only <c>content = "complete"</c> systems are treated as fully enumerable: for those, a code that
/// is absent is authoritatively "not a member" (<see cref="ContainsCode"/> returns false). For any
/// other content mode (<c>fragment</c>, <c>example</c>, <c>not-present</c>, ...) a miss is
/// undecidable and reported as null, so a downstream membership check degrades to a warning rather
/// than a false rejection. Hierarchical <c>concept.concept[]</c> nesting is flattened.
/// </para>
/// </summary>
public sealed class PackageCodeSystemSource : ICodeSystemProvider
{
    private readonly Dictionary<string, ExtractedResource> _codeSystems;
    private readonly ConcurrentDictionary<string, ParsedCodeSystem?> _parsed = new(StringComparer.Ordinal);
    private readonly ILogger<PackageCodeSystemSource> _logger;

    public PackageCodeSystemSource(
        IEnumerable<ExtractedResource> resources,
        ILogger<PackageCodeSystemSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _logger = logger ?? NullLogger<PackageCodeSystemSource>.Instance;
        _codeSystems = new Dictionary<string, ExtractedResource>(StringComparer.Ordinal);
        foreach (var r in resources)
        {
            if (r.ResourceType != "CodeSystem")
            {
                continue;
            }

            var canonical = StripVersionSuffix(r.Canonical);
            if (_codeSystems.ContainsKey(canonical))
            {
                _logger.LogWarning(
                    "Duplicate CodeSystem canonical '{Canonical}' — the later definition (id='{ResourceId}') overwrites the earlier one. Check package ordering if this is unintended.",
                    canonical,
                    r.ResourceId);
            }

            _codeSystems[canonical] = r;
        }
    }

    public string? GetDisplay(string system, string code)
    {
        if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(code))
        {
            return null;
        }

        var parsed = Parse(system);
        return parsed is not null && parsed.Concepts.TryGetValue(code, out var display) ? display : null;
    }

    public bool? ContainsCode(string system, string code)
    {
        if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(code))
        {
            return null;
        }

        var parsed = Parse(system);
        if (parsed is null)
        {
            return null;
        }

        if (parsed.Concepts.ContainsKey(code))
        {
            return true;
        }

        // A miss is only authoritative when the system fully enumerates its codes.
        return parsed.IsComplete ? false : null;
    }

    private ParsedCodeSystem? Parse(string system)
    {
        var canonical = StripVersionSuffix(system);
        return _parsed.GetOrAdd(canonical, key =>
        {
            if (!_codeSystems.TryGetValue(key, out var cs))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(cs.ResourceJson);
                var root = doc.RootElement;

                // Complete ONLY when explicitly declared: an absent or unknown 'content' leaves
                // completeness undecidable, so a code miss must degrade to null (undecidable), not an
                // authoritative "not a member" that would falsely reject a valid code.
                var isComplete = root.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String
                    && string.Equals(content.GetString(), "complete", StringComparison.Ordinal);

                var concepts = new Dictionary<string, string?>(StringComparer.Ordinal);
                if (root.TryGetProperty("concept", out var conceptArr) && conceptArr.ValueKind == JsonValueKind.Array)
                {
                    CollectConcepts(conceptArr, concepts);
                }

                return new ParsedCodeSystem(concepts, isComplete);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse CodeSystem JSON for system '{System}' — treating as unknown", key);
                return null;
            }
        });
    }

    private static void CollectConcepts(JsonElement conceptArr, Dictionary<string, string?> concepts)
    {
        foreach (var concept in conceptArr.EnumerateArray())
        {
            var code = concept.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            if (!string.IsNullOrEmpty(code))
            {
                var display = concept.TryGetProperty("display", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString()
                    : null;

                // First definition wins for a duplicated code within a system.
                concepts.TryAdd(code!, display);
            }

            // Hierarchical CodeSystems use nested concept[] — flatten them.
            if (concept.TryGetProperty("concept", out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                CollectConcepts(nested, concepts);
            }
        }
    }

    private static string StripVersionSuffix(string url)
    {
        var pipe = url.IndexOf('|', StringComparison.Ordinal);
        return pipe >= 0 ? url[..pipe] : url;
    }

    /// <summary>
    /// Parsed, flattened concept content for a single CodeSystem: the code&#8594;display map and
    /// whether the system fully enumerates its codes (<c>content = "complete"</c>).
    /// </summary>
    private sealed class ParsedCodeSystem(IReadOnlyDictionary<string, string?> concepts, bool isComplete)
    {
        public IReadOnlyDictionary<string, string?> Concepts { get; } = concepts;

        public bool IsComplete { get; } = isComplete;
    }
}
