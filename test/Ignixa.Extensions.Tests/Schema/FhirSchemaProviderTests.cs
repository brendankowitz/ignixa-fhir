// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain;
using Ignixa.Serialization;
using Ignixa.Specification.Schema;
using Xunit;

namespace Ignixa.Extensions.Tests.Schema;

public class FhirSchemaProviderTests
{
    [Fact]
    public void Constructor_WithR4_ShouldInitializeSuccessfully()
    {
        // Act
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal(FhirSpecification.R4, provider.Version);
        Assert.NotEmpty(provider.ResourceTypeNames);
    }

    [Fact]
    public void Constructor_WithR5_ShouldInitializeSuccessfully()
    {
        // Act
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R5);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal(FhirSpecification.R5, provider.Version);
        Assert.NotEmpty(provider.ResourceTypeNames);
    }

    [Fact]
    public void ResourceTypeNames_WithR4_ShouldContainCommonResources()
    {
        // Arrange
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        // Act
        var resourceTypes = provider.ResourceTypeNames;

        // Assert
        Assert.Contains("Patient", resourceTypes);
        Assert.Contains("Observation", resourceTypes);
        Assert.Contains("Practitioner", resourceTypes);
        Assert.Contains("Encounter", resourceTypes);
        Assert.Contains("Medication", resourceTypes);
        Assert.Contains("Bundle", resourceTypes);
    }

    [Fact]
    public void Provide_WithPatient_ShouldReturnStructureDefinition()
    {
        // Arrange
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        // Act
        var structureDefinition = provider.Provide("http://hl7.org/fhir/StructureDefinition/Patient");

        // Assert
        Assert.NotNull(structureDefinition);
        Assert.True(structureDefinition.IsResource);
    }

    [Fact]
    public void Provide_WithObservation_ShouldReturnStructureDefinition()
    {
        // Arrange
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        // Act
        var structureDefinition = provider.Provide("http://hl7.org/fhir/StructureDefinition/Observation");

        // Assert
        Assert.NotNull(structureDefinition);
        Assert.True(structureDefinition.IsResource);
    }

    [Fact]
    public void Provide_WithBundle_ShouldReturnStructureDefinition()
    {
        // Arrange
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        // Act
        var structureDefinition = provider.Provide("http://hl7.org/fhir/StructureDefinition/Bundle");

        // Assert
        Assert.NotNull(structureDefinition);
        Assert.True(structureDefinition.IsResource);
    }

    [Fact]
    public void Provide_WithDataType_ShouldReturnStructureDefinition()
    {
        // Arrange
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        // Act
        var structureDefinition = provider.Provide("http://hl7.org/fhir/StructureDefinition/HumanName");

        // Assert
        Assert.NotNull(structureDefinition);
        Assert.False(structureDefinition.IsResource);
    }

    [Fact]
    public void Provide_WithInvalidUrl_ShouldReturnNull()
    {
        // Arrange
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        // Act
        var structureDefinition = provider.Provide("http://invalid-url/StructureDefinition/Invalid");

        // Assert
        Assert.Null(structureDefinition);
    }

    [Theory]
    [InlineData(FhirSpecification.R4)]
    [InlineData(FhirSpecification.R5)]
    [InlineData(FhirSpecification.R4B)]
    [InlineData(FhirSpecification.Stu3)]
    public void Constructor_WithAllVersions_ShouldInitializeSuccessfully(FhirSpecification version)
    {
        // Act
        var provider = new FhirJsonSchemaStructureDefinitionSummaryProvider(version);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal(version, provider.Version);
        Assert.NotEmpty(provider.ResourceTypeNames);
        Assert.True(provider.ResourceTypeNames.Count >= 100, $"Should have at least 100 resource types for {version}");
    }
}
