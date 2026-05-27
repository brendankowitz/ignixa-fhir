// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.PackageManagement.Models;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// A FHIR IG package that has been downloaded, extracted and indexed for use in tests.
/// Provides O(1) lookup by canonical URL and resource-type filtering on top of
/// <see cref="PackageExtractionResult"/>.
/// </summary>
public sealed class TestFhirPackage
{
    private readonly Dictionary<string, ExtractedResource> _byCanonical;
    private readonly Dictionary<string, ExtractedResource> _byVersionedCanonical;
    private readonly ILookup<string, ExtractedResource> _byResourceType;

    internal TestFhirPackage(PackageExtractionResult extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        Manifest = extraction.Manifest;
        Resources = extraction.Resources;

        // Last writer wins for unversioned canonical so the highest-version entry is reachable
        // by canonical alone. Tests that need a specific version should use the versioned key.
        _byCanonical = new Dictionary<string, ExtractedResource>(StringComparer.Ordinal);
        _byVersionedCanonical = new Dictionary<string, ExtractedResource>(StringComparer.Ordinal);
        foreach (var resource in extraction.Resources)
        {
            _byCanonical[resource.Canonical] = resource;
            if (!string.IsNullOrEmpty(resource.Version))
            {
                _byVersionedCanonical[$"{resource.Canonical}|{resource.Version}"] = resource;
            }
        }

        _byResourceType = extraction.Resources.ToLookup(r => r.ResourceType, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the package manifest (name, version, FHIR version).
    /// </summary>
    public PackageManifest Manifest { get; }

    /// <summary>
    /// Gets all extracted conformance resources (StructureDefinition, ValueSet, CodeSystem, etc.).
    /// </summary>
    public IReadOnlyList<ExtractedResource> Resources { get; }

    /// <summary>
    /// Looks up a resource by its canonical URL. If <paramref name="canonical"/> includes a
    /// <c>|version</c> suffix it is honored; otherwise the unversioned canonical is matched.
    /// </summary>
    /// <param name="canonical">Canonical URL, optionally with <c>|version</c> suffix.</param>
    /// <returns>The matching resource, or null if not found.</returns>
    public ExtractedResource? FindByCanonical(string canonical)
    {
        ArgumentException.ThrowIfNullOrEmpty(canonical);

        if (_byVersionedCanonical.TryGetValue(canonical, out var versioned))
        {
            return versioned;
        }

        return _byCanonical.TryGetValue(canonical, out var unversioned) ? unversioned : null;
    }

    /// <summary>
    /// Gets all resources of a specific FHIR resource type.
    /// </summary>
    /// <param name="resourceType">FHIR resource type name (e.g. "StructureDefinition", "ValueSet").</param>
    public IEnumerable<ExtractedResource> OfResourceType(string resourceType)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        return _byResourceType[resourceType];
    }
}
