// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Ips.Metadata;

/// <summary>
/// Parses section metadata from Composition StructureDefinition snapshots.
/// Extracts LOINC codes, titles, cardinality, and entry profiles from section slices using FHIRPath.
/// </summary>
public class SectionMetadataParser(
    ISchema schema,
    ILogger<SectionMetadataParser> logger)
{
    private const string SectionPath = "Composition.section";

    /// <summary>
    /// Extracts all section slices from a Composition StructureDefinition.
    /// </summary>
    public IReadOnlyList<Section> ParseSections(StructureDefinitionJsonNode compositionProfile)
    {
        ArgumentNullException.ThrowIfNull(compositionProfile);

        // Convert to IElement for FHIRPath evaluation
        var structureDefElement = compositionProfile.ResourceNode.ToElement(schema);

        // Find all section slices using FHIRPath:
        // Filter for elements with both path starting with 'Composition.section:' AND a sliceName
        var sectionSliceElements = structureDefElement
            .Select("snapshot.element.where(path.startsWith('Composition.section:') and sliceName.exists())")
            .ToList();

        if (sectionSliceElements.Count == 0)
        {
            logger.LogWarning(
                "StructureDefinition {Url} has no section slices",
                compositionProfile.Url);
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
            var section = ParseSection(structureDefElement, sliceName!);
            if (section is not null)
            {
                sections.Add(section);
            }
        }

        logger.LogInformation(
            "Parsed {Count} sections from StructureDefinition {Url}",
            sections.Count,
            compositionProfile.Url);

        return sections;
    }

    private Section? ParseSection(IElement structureDefElement, string sliceName)
    {
        // Find root element: snapshot.element.where(path='Composition.section:{sliceName}')
        var rootPath = $"{SectionPath}:{sliceName}";
        var rootElement = structureDefElement
            .Select($"snapshot.element.where(path='{rootPath}')")
            .FirstOrDefault();

        if (rootElement is null)
        {
            logger.LogDebug("No root element found for slice {SliceName}", sliceName);
            return null;
        }

        // Extract LOINC code from code element
        var loincCode = ExtractLoincCode(structureDefElement, sliceName);
        if (loincCode is null)
        {
            logger.LogDebug(
                "No LOINC code found for section slice {SliceName}, skipping",
                sliceName);
            return null;
        }

        // Extract title from title element
        var title = ExtractTitle(structureDefElement, sliceName) ?? sliceName;

        // Extract cardinality from root element
        // Note: rootElement is already an IElement, we navigate to child properties
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

        // Extract entry profiles
        var (profile, resourceTypes) = ExtractEntryProfiles(structureDefElement, sliceName);

        logger.LogDebug(
            "Parsed section {SliceName}: code={Code}, title={Title}, cardinality={Cardinality}, resources={Resources}",
            sliceName,
            loincCode.Value.Code,
            title,
            cardinality,
            string.Join(", ", resourceTypes));

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

        // Try pattern first: snapshot.element.where(path='{path}').patternCodeableConcept.coding.where(system='http://loinc.org')
        var loincCoding = structureDefElement
            .Select($"snapshot.element.where(path='{codePath}').patternCodeableConcept.coding.where(system='http://loinc.org')")
            .FirstOrDefault();

        // If not found, try fixed: snapshot.element.where(path='{path}').fixedCodeableConcept.coding.where(system='http://loinc.org')
        loincCoding ??= structureDefElement
            .Select($"snapshot.element.where(path='{codePath}').fixedCodeableConcept.coding.where(system='http://loinc.org')")
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

        // Find Reference type with targetProfile: snapshot.element.where(path='{path}').type.where(code='Reference').targetProfile
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

    private string? ExtractResourceTypeFromProfile(string? profileUrl)
    {
        if (profileUrl is null)
        {
            return null;
        }

        var parts = profileUrl.Split('/');
        if (parts.Length < 2)
        {
            logger.LogDebug("Invalid profile URL format: {Url}", profileUrl);
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
}
