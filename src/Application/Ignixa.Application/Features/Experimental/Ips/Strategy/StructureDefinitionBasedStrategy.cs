// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Application.Features.Experimental.Ips.Common;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Ips.Strategy;

/// <summary>
/// IPS generation strategy built from a Composition StructureDefinition.
/// Parses section slices to extract metadata dynamically.
/// </summary>
public class StructureDefinitionBasedStrategy : IIpsGenerationStrategy
{
    private readonly StructureDefinitionJsonNode _compositionProfile;
    private readonly IReadOnlyList<Section> _sections;
    private readonly FrozenDictionary<string, Section> _sectionByResourceType;
    private readonly string _bundleProfile;
    private readonly string _compositionProfileUrl;

    public StructureDefinitionBasedStrategy(
        StructureDefinitionJsonNode compositionProfile,
        IReadOnlyList<Section> sections,
        string bundleProfile)
    {
        ArgumentNullException.ThrowIfNull(compositionProfile);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(bundleProfile);

        _compositionProfile = compositionProfile;
        _sections = sections;
        _bundleProfile = bundleProfile;
        _compositionProfileUrl = compositionProfile.Url ?? throw new ArgumentException("StructureDefinition must have a URL", nameof(compositionProfile));

        _sectionByResourceType = CreateSectionByResourceType(sections);
    }

    /// <inheritdoc />
    public string BundleProfile => _bundleProfile;

    /// <inheritdoc />
    public IReadOnlyList<Section> GetSections() => _sections;

    /// <inheritdoc />
    public bool ShouldIncludeResource(Section section, ResourceJsonNode resource, IpsContext context)
    {
        // Default implementation: include all resources that match section's resource types
        // Can be overridden by specific strategies for filtering
        return true;
    }

    /// <inheritdoc />
    public Section? ClassifyResource(ResourceJsonNode resource)
    {
        var resourceType = resource.ResourceType;
        return _sectionByResourceType.GetValueOrDefault(resourceType);
    }

    /// <inheritdoc />
    public ResourceJsonNode CreateAuthor(IpsContext context)
    {
        return IpsDefaults.CreateDefaultAuthor();
    }

    /// <inheritdoc />
    public string CreateTitle(IpsContext context)
    {
        // Default title format
        return $"Patient Summary as of {context.GenerationTime:yyyy-MM-dd}";
    }

    /// <inheritdoc />
    public void PostProcessBundle(ResourceJsonNode bundle, IpsContext context)
    {
        // No post-processing by default
    }

    private static FrozenDictionary<string, Section> CreateSectionByResourceType(
        IReadOnlyList<Section> sections)
    {
        var dict = new Dictionary<string, Section>();

        foreach (var section in sections)
        {
            foreach (var resourceType in section.ResourceTypes)
            {
                // First section wins for resource types that appear in multiple sections
                dict.TryAdd(resourceType, section);
            }
        }

        return dict.ToFrozenDictionary();
    }
}
