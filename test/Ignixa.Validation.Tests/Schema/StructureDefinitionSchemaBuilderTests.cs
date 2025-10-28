// <copyright file="StructureDefinitionSchemaBuilderTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using FluentAssertions;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Unit tests for StructureDefinitionSchemaBuilder.
/// Tests that it correctly builds ValidationSchema objects from IStructureDefinitionSummary metadata.
/// </summary>
public class StructureDefinitionSchemaBuilderTests
{
    private readonly R4StructureDefinitionSummaryProvider _provider;
    private readonly StructureDefinitionSchemaBuilder _builder;

    public StructureDefinitionSchemaBuilderTests()
    {
        _provider = new R4StructureDefinitionSummaryProvider();
        _builder = new StructureDefinitionSchemaBuilder();
    }

    #region Required Field Checks

    [Fact]
    public void GivenPatientStructure_WhenBuildingSchema_ThenCreatesRequiredChecks()
    {
        // Arrange
        var summary = _provider.Provide("Patient");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        schema.Should().NotBeNull();
        schema.ResourceType.Should().Be("Patient");
        schema.CanonicalUrl.Should().Be("http://hl7.org/fhir/StructureDefinition/Patient");

        // Note: Patient may not have many required elements in base profile
        // The builder creates CardinalityCheck objects for all elements (which includes required fields as min=1)
        var cardinalityChecks = schema.Checks.OfType<CardinalityCheck>().ToList();

        // All elements should have cardinality checks
        var elements = summary!.GetElements();
        var elementCount = elements.Count(e => !e.ElementName.Contains('.'));  // Top-level only
        cardinalityChecks.Should().HaveCount(elementCount);
    }

    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenCreatesCardinalityChecks()
    {
        // Arrange
        var summary = _provider.Provide("Observation");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        schema.Should().NotBeNull();
        schema.ResourceType.Should().Be("Observation");

        var cardinalityChecks = schema.Checks.OfType<CardinalityCheck>().ToList();
        cardinalityChecks.Should().NotBeEmpty();
    }

    #endregion

    #region Cardinality Checks

    [Fact]
    public void GivenPatientStructure_WhenBuildingSchema_ThenCreatesCardinalityChecks()
    {
        // Arrange
        var summary = _provider.Provide("Patient");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var cardinalityChecks = schema.Checks.OfType<CardinalityCheck>().ToList();
        cardinalityChecks.Should().NotBeEmpty();

        // Every element should have a cardinality check
        var elements = summary!.GetElements();
        cardinalityChecks.Should().HaveCount(elements.Count);
    }

    [Fact]
    public void GivenElementWithMinZero_WhenBuildingSchema_ThenCreatesCardinalityCheckWithMinZero()
    {
        // Arrange
        var summary = _provider.Provide("Patient");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var cardinalityChecks = schema.Checks.OfType<CardinalityCheck>().ToList();

        // Most elements should have min=0 (optional)
        var optionalElements = cardinalityChecks.Where(c =>
        {
            // Access private fields via reflection for testing
            var minField = typeof(CardinalityCheck).GetField("_min",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return minField != null && (int)minField.GetValue(c)! == 0;
        });

        optionalElements.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenRequiredElement_WhenBuildingSchema_ThenCreatesCardinalityCheckWithMinOne()
    {
        // Arrange
        // Use Observation which has required elements (code, status)
        var summary = _provider.Provide("Observation");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var cardinalityChecks = schema.Checks.OfType<CardinalityCheck>().ToList();

        // Required elements should have min=1
        var requiredElements = cardinalityChecks.Where(c =>
        {
            var minField = typeof(CardinalityCheck).GetField("_min",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return minField != null && (int)minField.GetValue(c)! == 1;
        });

        // Observation has required elements like 'status' and 'code'
        requiredElements.Should().NotBeEmpty();
    }

    #endregion

    #region Type Checks

    [Fact]
    public void GivenPatientStructure_WhenBuildingSchema_ThenCreatesTypeChecks()
    {
        // Arrange
        var summary = _provider.Provide("Patient");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var typeChecks = schema.Checks.OfType<TypeCheck>().ToList();
        typeChecks.Should().NotBeEmpty();

        // Should have type checks for primitive fields
        // Patient has primitive fields like 'active' (boolean), 'birthDate' (date), etc.
        var hasPrimitiveTypeCheck = typeChecks.Any(c =>
        {
            var typeField = typeof(TypeCheck).GetField("_expectedType",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return typeField != null && typeField.GetValue(c) is string type &&
                   (type == "boolean" || type == "date");
        });
        hasPrimitiveTypeCheck.Should().BeTrue();

        // Note: "id" element validation is handled by IdFormatCheck (universal check)
        // not by TypeCheck, because the "id" element is inherited from Resource base type
        // and may not be included in the schema's GetElements() collection
    }

    [Fact]
    public void GivenPrimitiveTypes_WhenBuildingSchema_ThenOnlyIncludesPrimitiveTypeChecks()
    {
        // Arrange
        var summary = _provider.Provide("Patient");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var typeChecks = schema.Checks.OfType<TypeCheck>().ToList();

        // All type checks should be for primitive types
        foreach (var check in typeChecks)
        {
            var typeField = typeof(TypeCheck).GetField("_expectedType",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var expectedType = typeField!.GetValue(check) as string;

            // Should be one of the primitive types
            var primitiveTypes = new[]
            {
                "id", "string", "uri", "url", "canonical",
                "date", "dateTime", "instant", "time",
                "boolean", "integer", "decimal", "positiveInt",
                "unsignedInt", "code", "oid", "uuid"
            };

            primitiveTypes.Should().Contain(expectedType);
        }
    }

    #endregion

    #region Reference Format Checks

    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenCreatesReferenceChecks()
    {
        // Arrange
        var summary = _provider.Provide("Observation");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var referenceChecks = schema.Checks.OfType<ReferenceFormatCheck>().ToList();

        // Observation has reference fields like 'subject', 'performer', etc.
        referenceChecks.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenPatientStructure_WhenBuildingSchema_ThenCreatesReferenceChecksForReferenceFields()
    {
        // Arrange
        var summary = _provider.Provide("Patient");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var referenceChecks = schema.Checks.OfType<ReferenceFormatCheck>().ToList();

        // Patient has reference fields like 'managingOrganization', 'generalPractitioner', etc.
        referenceChecks.Should().NotBeEmpty();
    }

    #endregion

    #region Coding Structure Checks

    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenCreatesCodingChecks()
    {
        // Arrange
        var summary = _provider.Provide("Observation");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var codingChecks = schema.Checks.OfType<CodingStructureCheck>().ToList();

        // Observation has CodeableConcept fields like 'code', 'category', etc.
        codingChecks.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenAllergyIntoleranceStructure_WhenBuildingSchema_ThenCreatesCodingChecksForCodeableConceptFields()
    {
        // Arrange
        var summary = _provider.Provide("AllergyIntolerance");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert
        var codingChecks = schema.Checks.OfType<CodingStructureCheck>().ToList();

        // AllergyIntolerance has multiple CodeableConcept fields
        codingChecks.Should().NotBeEmpty();
    }

    #endregion

    #region Error Handling

    [Fact]
    public void GivenNullSummary_WhenBuildingSchema_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new StructureDefinitionSchemaBuilder();

        // Act & Assert
        var act = () => builder.BuildSchema(null!, _provider);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("summary");
    }

    [Fact]
    public void GivenNullProvider_WhenBuildingSchema_ThenThrowsArgumentNullException()
    {
        // Arrange
        var summary = _provider.Provide("Patient");
        var builder = new StructureDefinitionSchemaBuilder();

        // Act & Assert
        var act = () => builder.BuildSchema(summary!, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("provider");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GivenMultipleResourceTypes_WhenBuildingSchemas_ThenEachHasUniqueCanonicalUrl()
    {
        // Arrange
        var resourceTypes = new[] { "Patient", "Observation", "Condition" };

        // Act
        var schemas = resourceTypes
            .Select(rt => _builder.BuildSchema(_provider.Provide(rt)!, _provider))
            .ToList();

        // Assert
        schemas.Should().HaveCount(3);
        schemas.Select(s => s.CanonicalUrl).Should().OnlyHaveUniqueItems();
        schemas.Should().Contain(s => s.CanonicalUrl == "http://hl7.org/fhir/StructureDefinition/Patient");
        schemas.Should().Contain(s => s.CanonicalUrl == "http://hl7.org/fhir/StructureDefinition/Observation");
        schemas.Should().Contain(s => s.CanonicalUrl == "http://hl7.org/fhir/StructureDefinition/Condition");
    }

    [Fact]
    public void GivenComplexResourceType_WhenBuildingSchema_ThenCreatesAllCheckTypes()
    {
        // Arrange
        var summary = _provider.Provide("Observation");

        // Act
        var schema = _builder.BuildSchema(summary!, _provider);

        // Assert - Observation should have all types of checks
        schema.Checks.Should().NotBeEmpty();
        schema.Checks.OfType<CardinalityCheck>().Should().NotBeEmpty();
        schema.Checks.OfType<TypeCheck>().Should().NotBeEmpty();
        schema.Checks.OfType<ReferenceFormatCheck>().Should().NotBeEmpty();
        schema.Checks.OfType<CodingStructureCheck>().Should().NotBeEmpty();
    }

    #endregion
}
