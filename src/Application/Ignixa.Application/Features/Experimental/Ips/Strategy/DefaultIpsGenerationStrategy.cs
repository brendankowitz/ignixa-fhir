// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Application.Features.Experimental.Ips.Common;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Ips.Strategy;

/// <summary>
/// Default IPS generation strategy implementing the IPS IG STU 2 specification.
/// </summary>
public class DefaultIpsGenerationStrategy : IIpsGenerationStrategy
{
    private static readonly IReadOnlyList<Section> Sections = CreateSections();
    private static readonly FrozenDictionary<string, Section> SectionByResourceType = CreateSectionByResourceType();

    /// <inheritdoc />
    public string BundleProfile => IpsConstants.DefaultBundleProfile;

    /// <inheritdoc />
    public IReadOnlyList<Section> GetSections() => Sections;

    /// <inheritdoc />
    public bool ShouldIncludeResource(Section section, ResourceJsonNode resource, IpsContext context)
    {
        var resourceType = resource.ResourceType;

        return resourceType switch
        {
            "AllergyIntolerance" => ShouldIncludeAllergy(resource),
            "Condition" => ShouldIncludeCondition(resource),
            "MedicationStatement" => ShouldIncludeMedicationStatement(resource),
            "MedicationRequest" => ShouldIncludeMedicationRequest(resource),
            "Immunization" => ShouldIncludeImmunization(resource),
            "Procedure" => true,
            "DeviceUseStatement" => true,
            "DiagnosticReport" => true,
            "Observation" => true,
            _ => true
        };
    }

    /// <inheritdoc />
    public Section? ClassifyResource(ResourceJsonNode resource)
    {
        var resourceType = resource.ResourceType;
        return SectionByResourceType.GetValueOrDefault(resourceType);
    }

    /// <inheritdoc />
    public ResourceJsonNode CreateAuthor(IpsContext context)
    {
        return IpsDefaults.CreateDefaultAuthor();
    }

    /// <inheritdoc />
    public string CreateTitle(IpsContext context)
    {
        return "Patient Summary as of " + context.GenerationTime.ToString("yyyy-MM-dd");
    }

    /// <inheritdoc />
    public void PostProcessBundle(ResourceJsonNode bundle, IpsContext context)
    {
        // No post-processing needed for default strategy
    }

    private static bool ShouldIncludeAllergy(ResourceJsonNode resource)
    {
        var clinicalStatus = GetCodeFromCodeableConcept(resource.MutableNode["clinicalStatus"]);
        if (clinicalStatus is "inactive" or "resolved")
        {
            return false;
        }

        var verificationStatus = GetCodeFromCodeableConcept(resource.MutableNode["verificationStatus"]);
        if (verificationStatus is "entered-in-error" or "refuted")
        {
            return false;
        }

        return true;
    }

    private static bool ShouldIncludeCondition(ResourceJsonNode resource)
    {
        var clinicalStatus = GetCodeFromCodeableConcept(resource.MutableNode["clinicalStatus"]);
        if (clinicalStatus is "inactive" or "resolved" or "remission")
        {
            return false;
        }

        var verificationStatus = GetCodeFromCodeableConcept(resource.MutableNode["verificationStatus"]);
        if (verificationStatus is "entered-in-error" or "refuted")
        {
            return false;
        }

        return true;
    }

    private static bool ShouldIncludeMedicationStatement(ResourceJsonNode resource)
    {
        var status = resource.MutableNode["status"]?.GetValue<string>();
        return status is null or "active" or "intended" or "unknown" or "on-hold";
    }

    private static bool ShouldIncludeMedicationRequest(ResourceJsonNode resource)
    {
        var status = resource.MutableNode["status"]?.GetValue<string>();
        return status is null or "active" or "on-hold" or "draft";
    }

    private static bool ShouldIncludeImmunization(ResourceJsonNode resource)
    {
        var status = resource.MutableNode["status"]?.GetValue<string>();
        return status is null or "completed";
    }

    private static string? GetCodeFromCodeableConcept(JsonNode? codeableConceptNode)
    {
        if (codeableConceptNode is not JsonObject codeableConcept)
        {
            return null;
        }

        if (codeableConcept["coding"] is JsonArray codingArray && codingArray.Count > 0)
        {
            return codingArray[0]?["code"]?.GetValue<string>();
        }

        return null;
    }

    private static IReadOnlyList<Section> CreateSections()
    {
        return
        [
            // Required sections (MUST include)
            new Section
            {
                Title = "Allergies and Intolerances",
                Code = IpsConstants.SectionCodes.Allergies,
                CodeSystem = IpsConstants.LoincSystem,
                Display = "Allergies and adverse reactions Document",
                Profile = "http://hl7.org/fhir/uv/ips/StructureDefinition/AllergyIntolerance-uv-ips",
                ResourceTypes = new HashSet<string> { "AllergyIntolerance" },
                Cardinality = SectionCardinality.Required
            },
            new Section
            {
                Title = "Medication Summary",
                Code = IpsConstants.SectionCodes.Medications,
                CodeSystem = IpsConstants.LoincSystem,
                Display = "History of Medication use Narrative",
                Profile = "http://hl7.org/fhir/uv/ips/StructureDefinition/MedicationStatement-uv-ips",
                ResourceTypes = new HashSet<string> { "MedicationStatement", "MedicationRequest" },
                Cardinality = SectionCardinality.Required
            },
            new Section
            {
                Title = "Problem List",
                Code = IpsConstants.SectionCodes.Problems,
                CodeSystem = IpsConstants.LoincSystem,
                Display = "Problem list - Reported",
                Profile = "http://hl7.org/fhir/uv/ips/StructureDefinition/Condition-uv-ips",
                ResourceTypes = new HashSet<string> { "Condition" },
                Cardinality = SectionCardinality.Required
            },

            // Recommended sections (SHOULD include if data exists)
            new Section
            {
                Title = "Immunizations",
                Code = IpsConstants.SectionCodes.Immunizations,
                CodeSystem = IpsConstants.LoincSystem,
                Display = "History of Immunization Narrative",
                Profile = "http://hl7.org/fhir/uv/ips/StructureDefinition/Immunization-uv-ips",
                ResourceTypes = new HashSet<string> { "Immunization" },
                Cardinality = SectionCardinality.Recommended
            },
            new Section
            {
                Title = "History of Procedures",
                Code = IpsConstants.SectionCodes.Procedures,
                CodeSystem = IpsConstants.LoincSystem,
                Display = "History of Procedures Document",
                Profile = "http://hl7.org/fhir/uv/ips/StructureDefinition/Procedure-uv-ips",
                ResourceTypes = new HashSet<string> { "Procedure" },
                Cardinality = SectionCardinality.Recommended
            },
            new Section
            {
                Title = "Medical Devices",
                Code = IpsConstants.SectionCodes.Devices,
                CodeSystem = IpsConstants.LoincSystem,
                Display = "History of medical device use",
                Profile = "http://hl7.org/fhir/uv/ips/StructureDefinition/DeviceUseStatement-uv-ips",
                ResourceTypes = new HashSet<string> { "DeviceUseStatement" },
                Cardinality = SectionCardinality.Recommended
            },
            new Section
            {
                Title = "Diagnostic Results",
                Code = IpsConstants.SectionCodes.DiagnosticResults,
                CodeSystem = IpsConstants.LoincSystem,
                Display = "Relevant diagnostic tests/laboratory data Narrative",
                Profile = "http://hl7.org/fhir/uv/ips/StructureDefinition/DiagnosticReport-uv-ips",
                ResourceTypes = new HashSet<string> { "DiagnosticReport", "Observation" },
                Cardinality = SectionCardinality.Recommended
            }
        ];
    }

    private static FrozenDictionary<string, Section> CreateSectionByResourceType()
    {
        var dict = new Dictionary<string, Section>();

        foreach (var section in Sections)
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
