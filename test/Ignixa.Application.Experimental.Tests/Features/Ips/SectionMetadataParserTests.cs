// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Application.Features.Experimental.Ips.Metadata;
using Ignixa.Serialization.Models;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Ips;

/// <summary>
/// Unit tests for <see cref="SectionMetadataParser"/>.
/// </summary>
public class SectionMetadataParserTests
{
    private readonly SectionMetadataParser _parser;
    private readonly ISchema _schema;

    public SectionMetadataParserTests()
    {
        _schema = new R4CoreSchemaProvider();
        _parser = new SectionMetadataParser(_schema, NullLogger<SectionMetadataParser>.Instance);
    }

    [Fact]
    public void GivenStructureDefinitionWithSections_WhenParsingSections_ThenReturnsAllValidSections()
    {
        // Arrange
        var structureDefinition = CreateIpsCompositionStructureDefinition();

        // Act
        var sections = _parser.ParseSections(structureDefinition);

        // Assert
        sections.ShouldNotBeEmpty();
        sections.Count.ShouldBe(2); // Two valid sections in our test fixture
    }

    [Fact]
    public void GivenAllergiesSection_WhenParsing_ThenExtractsCorrectMetadata()
    {
        // Arrange
        var structureDefinition = CreateIpsCompositionStructureDefinition();

        // Act
        var sections = _parser.ParseSections(structureDefinition);
        var allergiesSection = sections.FirstOrDefault(s => s.Code == "48765-2");

        // Assert
        allergiesSection.ShouldNotBeNull();
        allergiesSection.Title.ShouldBe("Allergies and Intolerances");
        allergiesSection.Code.ShouldBe("48765-2");
        allergiesSection.CodeSystem.ShouldBe("http://loinc.org");
        allergiesSection.Display.ShouldBe("Allergies and adverse reactions Document");
        allergiesSection.Cardinality.ShouldBe(SectionCardinality.Required);
        allergiesSection.ResourceTypes.ShouldContain("AllergyIntolerance");
        allergiesSection.Profile.ShouldBe("http://hl7.org/fhir/uv/ips/StructureDefinition/AllergyIntolerance-uv-ips");
    }

    [Fact]
    public void GivenMedicationsSection_WhenParsing_ThenExtractsMultipleResourceTypes()
    {
        // Arrange
        var structureDefinition = CreateIpsCompositionStructureDefinition();

        // Act
        var sections = _parser.ParseSections(structureDefinition);
        var medicationsSection = sections.FirstOrDefault(s => s.Code == "10160-0");

        // Assert
        medicationsSection.ShouldNotBeNull();
        medicationsSection.Title.ShouldBe("Medication Summary");
        medicationsSection.ResourceTypes.ShouldContain("MedicationStatement");
        medicationsSection.ResourceTypes.ShouldContain("MedicationRequest");
        medicationsSection.ResourceTypes.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenRecommendedSection_WhenParsing_ThenCardinalityIsRecommended()
    {
        // Arrange
        var json = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://example.org/test",
          "type": "Composition",
          "snapshot": {
            "element": [
              {
                "path": "Composition.section:sectionImmunizations",
                "sliceName": "sectionImmunizations",
                "min": 0,
                "max": "1"
              },
              {
                "path": "Composition.section:sectionImmunizations.code",
                "patternCodeableConcept": {
                  "coding": [{
                    "system": "http://loinc.org",
                    "code": "11369-6"
                  }]
                }
              },
              {
                "path": "Composition.section:sectionImmunizations.title",
                "fixedString": "Immunizations"
              },
              {
                "path": "Composition.section:sectionImmunizations.entry",
                "type": [{
                  "code": "Reference",
                  "targetProfile": [
                    "http://hl7.org/fhir/uv/ips/StructureDefinition/Immunization-uv-ips"
                  ]
                }]
              }
            ]
          }
        }
        """;

        var sd = StructureDefinitionJsonNode.Parse(json, NullLogger.Instance);
        sd.ShouldNotBeNull();

        // Act
        var sections = _parser.ParseSections(sd);

        // Assert
        sections.Count.ShouldBe(1);
        sections[0].Cardinality.ShouldBe(SectionCardinality.Recommended);
    }

    [Fact]
    public void GivenStructureDefinitionWithNoSnapshot_WhenParsing_ThenReturnsEmptyList()
    {
        // Arrange
        var json = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://example.org/test",
          "type": "Composition"
        }
        """;

        var sd = StructureDefinitionJsonNode.Parse(json, NullLogger.Instance);
        sd.ShouldNotBeNull();

        // Act
        var sections = _parser.ParseSections(sd);

        // Assert
        sections.ShouldBeEmpty();
    }

    [Fact]
    public void GivenSectionWithoutLoincCode_WhenParsing_ThenSectionIsSkipped()
    {
        // Arrange
        var json = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://example.org/test",
          "type": "Composition",
          "snapshot": {
            "element": [
              {
                "path": "Composition.section:sectionCustom",
                "sliceName": "sectionCustom",
                "min": 0,
                "max": "1"
              },
              {
                "path": "Composition.section:sectionCustom.title",
                "fixedString": "Custom Section"
              }
            ]
          }
        }
        """;

        var sd = StructureDefinitionJsonNode.Parse(json, NullLogger.Instance);
        sd.ShouldNotBeNull();

        // Act
        var sections = _parser.ParseSections(sd);

        // Assert
        sections.ShouldBeEmpty();
    }

    [Fact]
    public void GivenSectionWithFixedCodeableConcept_WhenParsing_ThenExtractsLoincCode()
    {
        // Arrange
        var json = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://example.org/test",
          "type": "Composition",
          "snapshot": {
            "element": [
              {
                "path": "Composition.section:sectionTest",
                "sliceName": "sectionTest",
                "min": 1,
                "max": "1"
              },
              {
                "path": "Composition.section:sectionTest.code",
                "fixedCodeableConcept": {
                  "coding": [{
                    "system": "http://loinc.org",
                    "code": "99999-9",
                    "display": "Test Section"
                  }]
                }
              },
              {
                "path": "Composition.section:sectionTest.title",
                "fixedString": "Test"
              },
              {
                "path": "Composition.section:sectionTest.entry",
                "type": [{
                  "code": "Reference",
                  "targetProfile": [
                    "http://hl7.org/fhir/StructureDefinition/Observation"
                  ]
                }]
              }
            ]
          }
        }
        """;

        var sd = StructureDefinitionJsonNode.Parse(json, NullLogger.Instance);
        sd.ShouldNotBeNull();

        // Act
        var sections = _parser.ParseSections(sd);

        // Assert
        sections.Count.ShouldBe(1);
        sections[0].Code.ShouldBe("99999-9");
        sections[0].Display.ShouldBe("Test Section");
    }

    [Fact]
    public void GivenSectionWithPatternString_WhenParsing_ThenExtractsTitle()
    {
        // Arrange
        var json = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://example.org/test",
          "type": "Composition",
          "snapshot": {
            "element": [
              {
                "path": "Composition.section:sectionTest",
                "sliceName": "sectionTest",
                "min": 0,
                "max": "1"
              },
              {
                "path": "Composition.section:sectionTest.code",
                "patternCodeableConcept": {
                  "coding": [{
                    "system": "http://loinc.org",
                    "code": "88888-8"
                  }]
                }
              },
              {
                "path": "Composition.section:sectionTest.title",
                "patternString": "Pattern Title"
              },
              {
                "path": "Composition.section:sectionTest.entry",
                "type": [{
                  "code": "Reference",
                  "targetProfile": [
                    "http://hl7.org/fhir/StructureDefinition/Condition"
                  ]
                }]
              }
            ]
          }
        }
        """;

        var sd = StructureDefinitionJsonNode.Parse(json, NullLogger.Instance);
        sd.ShouldNotBeNull();

        // Act
        var sections = _parser.ParseSections(sd);

        // Assert
        sections.Count.ShouldBe(1);
        sections[0].Title.ShouldBe("Pattern Title");
    }

    [Fact]
    public void GivenSectionWithNoTitle_WhenParsing_ThenUsesSliceName()
    {
        // Arrange
        var json = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://example.org/test",
          "type": "Composition",
          "snapshot": {
            "element": [
              {
                "path": "Composition.section:sectionCustomName",
                "sliceName": "sectionCustomName",
                "min": 0,
                "max": "1"
              },
              {
                "path": "Composition.section:sectionCustomName.code",
                "patternCodeableConcept": {
                  "coding": [{
                    "system": "http://loinc.org",
                    "code": "77777-7"
                  }]
                }
              },
              {
                "path": "Composition.section:sectionCustomName.entry",
                "type": [{
                  "code": "Reference",
                  "targetProfile": [
                    "http://hl7.org/fhir/StructureDefinition/Patient"
                  ]
                }]
              }
            ]
          }
        }
        """;

        var sd = StructureDefinitionJsonNode.Parse(json, NullLogger.Instance);
        sd.ShouldNotBeNull();

        // Act
        var sections = _parser.ParseSections(sd);

        // Assert
        sections.Count.ShouldBe(1);
        sections[0].Title.ShouldBe("sectionCustomName");
    }

    private static StructureDefinitionJsonNode CreateIpsCompositionStructureDefinition()
    {
        var json = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://hl7.org/fhir/uv/ips/StructureDefinition/Composition-uv-ips",
          "type": "Composition",
          "snapshot": {
            "element": [
              {
                "path": "Composition.section:sectionAllergies",
                "sliceName": "sectionAllergies",
                "min": 1,
                "max": "1"
              },
              {
                "path": "Composition.section:sectionAllergies.title",
                "fixedString": "Allergies and Intolerances"
              },
              {
                "path": "Composition.section:sectionAllergies.code",
                "patternCodeableConcept": {
                  "coding": [{
                    "system": "http://loinc.org",
                    "code": "48765-2",
                    "display": "Allergies and adverse reactions Document"
                  }]
                }
              },
              {
                "path": "Composition.section:sectionAllergies.entry",
                "type": [{
                  "code": "Reference",
                  "targetProfile": [
                    "http://hl7.org/fhir/uv/ips/StructureDefinition/AllergyIntolerance-uv-ips"
                  ]
                }]
              },
              {
                "path": "Composition.section:sectionMedications",
                "sliceName": "sectionMedications",
                "min": 1,
                "max": "1"
              },
              {
                "path": "Composition.section:sectionMedications.title",
                "fixedString": "Medication Summary"
              },
              {
                "path": "Composition.section:sectionMedications.code",
                "patternCodeableConcept": {
                  "coding": [{
                    "system": "http://loinc.org",
                    "code": "10160-0",
                    "display": "History of Medication use Narrative"
                  }]
                }
              },
              {
                "path": "Composition.section:sectionMedications.entry",
                "type": [{
                  "code": "Reference",
                  "targetProfile": [
                    "http://hl7.org/fhir/uv/ips/StructureDefinition/MedicationStatement-uv-ips",
                    "http://hl7.org/fhir/uv/ips/StructureDefinition/MedicationRequest-uv-ips"
                  ]
                }]
              }
            ]
          }
        }
        """;

        var sd = StructureDefinitionJsonNode.Parse(json, NullLogger.Instance);
        sd.ShouldNotBeNull();
        return sd;
    }
}
