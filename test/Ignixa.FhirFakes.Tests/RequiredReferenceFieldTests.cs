// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests to ensure required Reference fields are populated in generated resources.
/// Regression test for bug where required Reference fields were not being generated.
/// </summary>
public class RequiredReferenceFieldTests
{
    [Fact]
    public void GivenR4Schema_WhenGeneratingAllergyIntolerance_ThenPatientFieldIsPopulated()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        // Act
        var allergy = faker.Generate("AllergyIntolerance");

        // Assert
        allergy.ShouldNotBeNull();
        allergy.ResourceType.ShouldBe("AllergyIntolerance");

        // Verify patient field is present and is a valid reference
        var patientNode = allergy.MutableNode["patient"];
        patientNode.ShouldNotBeNull("patient is a required field in AllergyIntolerance");

        var referenceNode = patientNode?["reference"];
        referenceNode.ShouldNotBeNull();
        referenceNode!.ToString().ShouldStartWith("Patient/", Case.Sensitive);
    }

    [Fact]
    public void GivenStu3Schema_WhenGeneratingAllergyIntolerance_ThenPatientFieldIsPopulated()
    {
        // Arrange
        var schemaProvider = new STU3CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        // Act
        var allergy = faker.Generate("AllergyIntolerance");

        // Assert
        allergy.ShouldNotBeNull();
        allergy.ResourceType.ShouldBe("AllergyIntolerance");

        // Verify patient field is present and is a valid reference
        var patientNode = allergy.MutableNode["patient"];
        patientNode.ShouldNotBeNull("patient is a required field in AllergyIntolerance");

        var referenceNode = patientNode?["reference"];
        referenceNode.ShouldNotBeNull();
        referenceNode!.ToString().ShouldStartWith("Patient/", Case.Sensitive);
    }

    [Fact]
    public void GivenR5Schema_WhenGeneratingAllergyIntolerance_ThenPatientFieldIsPopulated()
    {
        // Arrange
        var schemaProvider = new R5CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        // Act
        var allergy = faker.Generate("AllergyIntolerance");

        // Assert
        allergy.ShouldNotBeNull();
        allergy.ResourceType.ShouldBe("AllergyIntolerance");

        // Verify patient field is present and is a valid reference
        var patientNode = allergy.MutableNode["patient"];
        patientNode.ShouldNotBeNull("patient is a required field in AllergyIntolerance");

        var referenceNode = patientNode?["reference"];
        referenceNode.ShouldNotBeNull();
        referenceNode!.ToString().ShouldStartWith("Patient/", Case.Sensitive);
    }
}
