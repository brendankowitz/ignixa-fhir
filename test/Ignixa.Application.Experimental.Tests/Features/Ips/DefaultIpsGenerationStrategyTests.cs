// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Application.Features.Experimental.Ips.Strategy;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.Application.Experimental.Tests.Features.Ips;

/// <summary>
/// Unit tests for <see cref="DefaultIpsGenerationStrategy"/>.
/// </summary>
public class DefaultIpsGenerationStrategyTests
{
    private readonly DefaultIpsGenerationStrategy _strategy;

    public DefaultIpsGenerationStrategyTests()
    {
        _strategy = new DefaultIpsGenerationStrategy();
    }

    [Fact]
    public void GivenDefaultStrategy_WhenGettingBundleProfile_ThenReturnsIpsProfile()
    {
        // Act
        var profile = _strategy.BundleProfile;

        // Assert
        profile.ShouldBe(IpsConstants.DefaultBundleProfile);
    }

    [Fact]
    public void GivenDefaultStrategy_WhenGettingSections_ThenReturnsRequiredSections()
    {
        // Act
        var sections = _strategy.GetSections();

        // Assert
        sections.ShouldNotBeEmpty();
        sections.Count.ShouldBeGreaterThanOrEqualTo(3); // At least 3 required sections

        // Verify required sections exist
        sections.ShouldContain(s => s.Code == IpsConstants.SectionCodes.Allergies);
        sections.ShouldContain(s => s.Code == IpsConstants.SectionCodes.Medications);
        sections.ShouldContain(s => s.Code == IpsConstants.SectionCodes.Problems);
    }

    [Fact]
    public void GivenDefaultStrategy_WhenGettingSections_ThenRequiredSectionsHaveCorrectCardinality()
    {
        // Act
        var sections = _strategy.GetSections();

        // Assert
        var allergiesSection = sections.First(s => s.Code == IpsConstants.SectionCodes.Allergies);
        allergiesSection.Cardinality.ShouldBe(SectionCardinality.Required);

        var medicationsSection = sections.First(s => s.Code == IpsConstants.SectionCodes.Medications);
        medicationsSection.Cardinality.ShouldBe(SectionCardinality.Required);

        var problemsSection = sections.First(s => s.Code == IpsConstants.SectionCodes.Problems);
        problemsSection.Cardinality.ShouldBe(SectionCardinality.Required);
    }

    [Fact]
    public void GivenDefaultStrategy_WhenGettingSections_ThenSectionsHaveCorrectResourceTypes()
    {
        // Act
        var sections = _strategy.GetSections();

        // Assert
        var allergiesSection = sections.First(s => s.Code == IpsConstants.SectionCodes.Allergies);
        allergiesSection.ResourceTypes.ShouldContain("AllergyIntolerance");

        var medicationsSection = sections.First(s => s.Code == IpsConstants.SectionCodes.Medications);
        medicationsSection.ResourceTypes.ShouldContain("MedicationStatement");
        medicationsSection.ResourceTypes.ShouldContain("MedicationRequest");

        var problemsSection = sections.First(s => s.Code == IpsConstants.SectionCodes.Problems);
        problemsSection.ResourceTypes.ShouldContain("Condition");
    }

    [Fact]
    public void GivenAllergyIntolerance_WhenClassifying_ThenReturnsAllergiesSection()
    {
        // Arrange
        var json = """
            {
                "resourceType": "AllergyIntolerance",
                "id": "allergy-1",
                "clinicalStatus": {
                    "coding": [{ "code": "active" }]
                }
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var section = _strategy.ClassifyResource(resource);

        // Assert
        section.ShouldNotBeNull();
        section!.Code.ShouldBe(IpsConstants.SectionCodes.Allergies);
    }

    [Fact]
    public void GivenCondition_WhenClassifying_ThenReturnsProblemsSection()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Condition",
                "id": "condition-1",
                "clinicalStatus": {
                    "coding": [{ "code": "active" }]
                }
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var section = _strategy.ClassifyResource(resource);

        // Assert
        section.ShouldNotBeNull();
        section!.Code.ShouldBe(IpsConstants.SectionCodes.Problems);
    }

    [Fact]
    public void GivenMedicationStatement_WhenClassifying_ThenReturnsMedicationsSection()
    {
        // Arrange
        var json = """
            {
                "resourceType": "MedicationStatement",
                "id": "med-1",
                "status": "active"
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var section = _strategy.ClassifyResource(resource);

        // Assert
        section.ShouldNotBeNull();
        section!.Code.ShouldBe(IpsConstants.SectionCodes.Medications);
    }

    [Fact]
    public void GivenUnknownResourceType_WhenClassifying_ThenReturnsNull()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "patient-1"
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var section = _strategy.ClassifyResource(resource);

        // Assert
        section.ShouldBeNull();
    }

    [Fact]
    public void GivenActiveAllergy_WhenCheckingInclusion_ThenReturnsTrue()
    {
        // Arrange
        var json = """
            {
                "resourceType": "AllergyIntolerance",
                "id": "allergy-1",
                "clinicalStatus": {
                    "coding": [{ "code": "active" }]
                },
                "verificationStatus": {
                    "coding": [{ "code": "confirmed" }]
                }
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        var section = _strategy.GetSections().First(s => s.Code == IpsConstants.SectionCodes.Allergies);
        var context = CreateTestContext();

        // Act
        var result = _strategy.ShouldIncludeResource(section, resource, context);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenInactiveAllergy_WhenCheckingInclusion_ThenReturnsFalse()
    {
        // Arrange
        var json = """
            {
                "resourceType": "AllergyIntolerance",
                "id": "allergy-1",
                "clinicalStatus": {
                    "coding": [{ "code": "inactive" }]
                }
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        var section = _strategy.GetSections().First(s => s.Code == IpsConstants.SectionCodes.Allergies);
        var context = CreateTestContext();

        // Act
        var result = _strategy.ShouldIncludeResource(section, resource, context);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenResolvedCondition_WhenCheckingInclusion_ThenReturnsFalse()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Condition",
                "id": "condition-1",
                "clinicalStatus": {
                    "coding": [{ "code": "resolved" }]
                }
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        var section = _strategy.GetSections().First(s => s.Code == IpsConstants.SectionCodes.Problems);
        var context = CreateTestContext();

        // Act
        var result = _strategy.ShouldIncludeResource(section, resource, context);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenEnteredInErrorAllergy_WhenCheckingInclusion_ThenReturnsFalse()
    {
        // Arrange
        var json = """
            {
                "resourceType": "AllergyIntolerance",
                "id": "allergy-1",
                "verificationStatus": {
                    "coding": [{ "code": "entered-in-error" }]
                }
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        var section = _strategy.GetSections().First(s => s.Code == IpsConstants.SectionCodes.Allergies);
        var context = CreateTestContext();

        // Act
        var result = _strategy.ShouldIncludeResource(section, resource, context);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenActiveMedicationStatement_WhenCheckingInclusion_ThenReturnsTrue()
    {
        // Arrange
        var json = """
            {
                "resourceType": "MedicationStatement",
                "id": "med-1",
                "status": "active"
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        var section = _strategy.GetSections().First(s => s.Code == IpsConstants.SectionCodes.Medications);
        var context = CreateTestContext();

        // Act
        var result = _strategy.ShouldIncludeResource(section, resource, context);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenStoppedMedicationStatement_WhenCheckingInclusion_ThenReturnsFalse()
    {
        // Arrange
        var json = """
            {
                "resourceType": "MedicationStatement",
                "id": "med-1",
                "status": "stopped"
            }
            """;
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        var section = _strategy.GetSections().First(s => s.Code == IpsConstants.SectionCodes.Medications);
        var context = CreateTestContext();

        // Act
        var result = _strategy.ShouldIncludeResource(section, resource, context);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenContext_WhenCreatingAuthor_ThenReturnsDeviceResource()
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        var author = _strategy.CreateAuthor(context);

        // Assert
        author.ShouldNotBeNull();
        author.ResourceType.ShouldBe("Device");
        author.Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenContext_WhenCreatingTitle_ThenReturnsFormattedTitle()
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        var title = _strategy.CreateTitle(context);

        // Assert
        title.ShouldNotBeNullOrEmpty();
        title.ShouldStartWith("Patient Summary as of ");
    }

    private static IpsContext CreateTestContext()
    {
        var patientJson = """
            {
                "resourceType": "Patient",
                "id": "test-patient"
            }
            """;

        return new IpsContext
        {
            PatientId = "test-patient",
            Patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(patientJson),
            Strategy = new DefaultIpsGenerationStrategy(),
            PartitionId = 1,
            GenerationTime = DateTimeOffset.UtcNow
        };
    }
}
