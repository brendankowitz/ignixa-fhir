// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Application.Features.Experimental.Ips.Common;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Ips.Strategy;

/// <summary>
/// Factory for creating IPS generation strategies from Composition StructureDefinitions using FHIRPath.
/// </summary>
public class StructureDefinitionStrategyFactory(
    ISchema schema,
    ILogger<StructureDefinitionStrategyFactory> logger
) : IStructureDefinitionStrategyFactory
{
    private const string SectionPath = "Composition.section";
    private const string LoincSystem = "http://loinc.org";
    private const string IpsCompositionProfile = "http://hl7.org/fhir/uv/ips/StructureDefinition/Composition-uv-ips";

    public IIpsGenerationStrategy? CreateFromStructureDefinition(
        ResourceJsonNode compositionProfile,
        CancellationToken cancellationToken = default)
    {
        if (!IsPatientSummaryProfile(compositionProfile))
        {
            return null;
        }

        var element = compositionProfile.ToElement(schema);
        var url = element.Scalar("url") as string;
        logger.LogInformation(
            "Creating patient summary strategy from StructureDefinition {Url}",
            url);

        var sections = ParseSections(compositionProfile);

        if (sections.Count == 0)
        {
            logger.LogWarning(
                "No sections found in Composition profile {Url}",
                url);
            return null;
        }

        var bundleProfile = InferBundleProfile(url);

        var strategy = new StructureDefinitionBasedStrategy(
            sections,
            bundleProfile);

        logger.LogInformation(
            "Created strategy with {SectionCount} sections for profile {Profile}",
            sections.Count,
            bundleProfile);

        return strategy;
    }

    public bool IsPatientSummaryProfile(ResourceJsonNode structureDefinition)
    {
        var element = structureDefinition.ToElement(schema);

        // Check if type is Composition
        var type = element.Scalar("type") as string;
        if (type != "Composition")
        {
            return false;
        }

        if (IsIpsDerivedProfile(element))
        {
            return true;
        }

        if (HasSectionSlicingWithLoincCodes(element))
        {
            return true;
        }

        return false;
    }

    private bool IsIpsDerivedProfile(IElement structureDefElement)
    {
        // Get baseDefinition: baseDefinition
        var baseDefinition = structureDefElement.Scalar("baseDefinition") as string;

        if (baseDefinition == IpsCompositionProfile)
        {
            return true;
        }

        return baseDefinition?.Contains("/ips/", StringComparison.Ordinal) == true;
    }

    private bool HasSectionSlicingWithLoincCodes(IElement structureDefElement)
    {
        // Check for section slicing: snapshot.element with path='Composition.section' AND slicing.exists()
        var hasSectionSlicing = structureDefElement
            .Select($"snapshot.element.where(path = '{SectionPath}' and slicing.exists())")
            .Any();

        if (!hasSectionSlicing)
        {
            return false;
        }

        // Check for LOINC codes in section slices:
        // Elements with path starting with 'Composition.section:' AND ending with '.code'
        var hasLoincCodes = structureDefElement
            .Select($"snapshot.element.where(path.startsWith('{SectionPath}:') and path.endsWith('.code')).patternCodeableConcept.coding.where(system = '{LoincSystem}')")
            .Any();

        return hasLoincCodes;
    }

    private IReadOnlyList<Section> ParseSections(ResourceJsonNode compositionProfile)
    {
        var element = compositionProfile.ToElement(schema);

        // Find all section slices using FHIRPath
        // Filter for elements with path starting with 'Composition.section:' AND a sliceName
        var sectionSliceElements = element
            .Select("snapshot.element.where(path.startsWith('Composition.section:') and sliceName.exists())")
            .ToList();

        if (sectionSliceElements.Count == 0)
        {
            return [];
        }

        // Group by sliceName
        var sliceNames = sectionSliceElements
            .Select(e => e.Scalar("sliceName") as string)
            .Where(s => s is not null)
            .Distinct()
            .ToList();

        var sections = new List<Section>();
        foreach (var sliceName in sliceNames)
        {
            var section = ParseSection(element, sliceName!);
            if (section is not null)
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    private Section? ParseSection(IElement structureDefElement, string sliceName)
    {
        var rootPath = $"{SectionPath}:{sliceName}";
        var rootElement = structureDefElement
            .Select($"snapshot.element.where(path='{rootPath}')")
            .FirstOrDefault();

        if (rootElement is null)
        {
            return null;
        }

        var loincCode = ExtractLoincCode(structureDefElement, sliceName);
        if (loincCode is null)
        {
            return null;
        }

        var title = ExtractTitle(structureDefElement, sliceName) ?? sliceName;

        var minChildren = rootElement.Children("min");
        var maxChildren = rootElement.Children("max");

        var minChild = minChildren.Count > 0 ? minChildren[0] : null;
        var maxChild = maxChildren.Count > 0 ? maxChildren[0] : null;

        var min = minChild?.Value switch
        {
            int i => (int?)i,
            long l => (int?)l,
            _ => null
        };
        var max = maxChild?.Value as string;

        var cardinality = DetermineCardinality(min, max);

        var (profile, resourceTypes) = ExtractEntryProfiles(structureDefElement, sliceName);

        return new Section
        {
            Title = title,
            Code = loincCode.Value.Code,
            CodeSystem = loincCode.Value.System,
            Display = loincCode.Value.Display,
            Profile = profile,
            ResourceTypes = resourceTypes.ToHashSet(),
            Cardinality = cardinality
        };
    }

    private (string Code, string System, string? Display)? ExtractLoincCode(
        IElement structureDefElement,
        string sliceName)
    {
        var codePath = $"{SectionPath}:{sliceName}.code";

        // Try pattern first
        var loincCoding = structureDefElement
            .Select($"snapshot.element.where(path='{codePath}').patternCodeableConcept.coding.where(system='{LoincSystem}')")
            .FirstOrDefault();

        // If not found, try fixed
        loincCoding ??= structureDefElement
            .Select($"snapshot.element.where(path='{codePath}').fixedCodeableConcept.coding.where(system='{LoincSystem}')")
            .FirstOrDefault();

        if (loincCoding is null)
        {
            return null;
        }

        var code = loincCoding.Scalar("code") as string;
        var system = loincCoding.Scalar("system") as string;
        var display = loincCoding.Scalar("display") as string;

        if (code is null || system is null)
        {
            return null;
        }

        return (code, system, display);
    }

    private string? ExtractTitle(IElement structureDefElement, string sliceName)
    {
        var titlePath = $"{SectionPath}:{sliceName}.title";

        // Try fixedString first
        var title = structureDefElement.Scalar($"snapshot.element.where(path='{titlePath}').fixedString") as string;

        // If not found, try patternString
        title ??= structureDefElement.Scalar($"snapshot.element.where(path='{titlePath}').patternString") as string;

        return title;
    }

    private SectionCardinality DetermineCardinality(int? min, string? max)
    {
        if (min == 1 && max == "1")
        {
            return SectionCardinality.Required;
        }

        return SectionCardinality.Recommended;
    }

    private (string Profile, List<string> ResourceTypes) ExtractEntryProfiles(
        IElement structureDefElement,
        string sliceName)
    {
        var entryPath = $"{SectionPath}:{sliceName}.entry";

        // Find Reference type with targetProfile
        var targetProfiles = structureDefElement
            .Select($"snapshot.element.where(path='{entryPath}').type.where(code='Reference').targetProfile")
            .Select(e => e.Value as string)
            .Where(p => p is not null)
            .ToList();

        if (targetProfiles.Count == 0)
        {
            return ("http://hl7.org/fhir/StructureDefinition/Resource", []);
        }

        var resourceTypes = targetProfiles
            .Select(p => ExtractResourceTypeFromProfile(p!))
            .Where(rt => rt is not null)
            .Cast<string>()
            .Distinct()
            .ToList();

        return (targetProfiles[0]!, resourceTypes);
    }

    private string? ExtractResourceTypeFromProfile(string profileUrl)
    {
        var parts = profileUrl.Split('/');
        if (parts.Length < 2)
        {
            return null;
        }

        var lastPart = parts[^1];

        var hyphenIndex = lastPart.IndexOf('-', StringComparison.Ordinal);
        if (hyphenIndex > 0)
        {
            return lastPart[..hyphenIndex];
        }

        return lastPart;
    }

    private string InferBundleProfile(string? compositionUrl)
    {
        if (compositionUrl is null)
        {
            return "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips";
        }

        if (compositionUrl.Contains("Composition-uv-ips", StringComparison.Ordinal))
        {
            return compositionUrl.Replace("Composition-uv-ips", "Bundle-uv-ips", StringComparison.Ordinal);
        }

        var bundleUrl = compositionUrl.Replace("Composition", "Bundle", StringComparison.Ordinal);

        logger.LogWarning(
            "Using inferred Bundle profile {BundleUrl} for Composition {CompositionUrl}",
            bundleUrl,
            compositionUrl);

        return bundleUrl;
    }

    private sealed class StructureDefinitionBasedStrategy(
        IReadOnlyList<Section> sections,
        string bundleProfile
    ) : IIpsGenerationStrategy
    {
        private readonly FrozenDictionary<string, Section> _sectionByResourceType = CreateSectionByResourceType(sections);

        public string BundleProfile { get; } = bundleProfile;

        public IReadOnlyList<Section> GetSections() => sections;

        public bool ShouldIncludeResource(Section section, ResourceJsonNode resource, IpsContext context) => true;

        public Section? ClassifyResource(ResourceJsonNode resource)
        {
            var resourceType = resource.ResourceType;
            return _sectionByResourceType.GetValueOrDefault(resourceType);
        }

        public ResourceJsonNode CreateAuthor(IpsContext context)
        {
            return IpsDefaults.CreateDefaultAuthor();
        }

        public string CreateTitle(IpsContext context)
        {
            return $"Patient Summary as of {context.GenerationTime:yyyy-MM-dd}";
        }

        public void PostProcessBundle(ResourceJsonNode bundle, IpsContext context)
        {
        }

        private static FrozenDictionary<string, Section> CreateSectionByResourceType(
            IReadOnlyList<Section> sections)
        {
            var dict = new Dictionary<string, Section>();

            foreach (var section in sections)
            {
                foreach (var resourceType in section.ResourceTypes)
                {
                    dict.TryAdd(resourceType, section);
                }
            }

            return dict.ToFrozenDictionary();
        }
    }
}

