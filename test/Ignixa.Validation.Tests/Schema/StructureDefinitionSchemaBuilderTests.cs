// <copyright file="StructureDefinitionSchemaBuilderTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Unit tests for StructureDefinitionSchemaBuilder.
/// Tests that it correctly builds ValidationSchema objects from IStructureDefinitionSummary metadata.
/// </summary>
public class StructureDefinitionSchemaBuilderTests
{
    private readonly ISchema _schema;
    private readonly StructureDefinitionSchemaBuilder _builder;

    public StructureDefinitionSchemaBuilderTests()
    {
        _schema = new R4CoreSchemaProvider();
        _builder = new StructureDefinitionSchemaBuilder();
    }

    #region Required Field Checks

    [Fact]
    public void GivenPatientStructure_WhenBuildingSchema_ThenCreatesRequiredChecks()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Patient");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        schema.ShouldNotBeNull();
        schema.ResourceType.ShouldBe("Patient");
        schema.CanonicalUrl.ShouldBe("http://hl7.org/fhir/StructureDefinition/Patient");

        // Note: Patient may not have many required elements in base profile
        // The builder creates CardinalityCheck objects for all elements (which includes required fields as min=1)
        var cardinalityChecks = schema.Checks.OfType<CardinalityCheck>().ToList();

        // All elements should have cardinality checks
        var elements = typeDefinition!.Children;
        var elementCount = elements.Count(e => !e.Info.Name.Contains('.'));  // Top-level only
        cardinalityChecks.Count.ShouldBe(elementCount);
    }

    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenCreatesCardinalityChecks()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Observation");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        schema.ShouldNotBeNull();
        schema.ResourceType.ShouldBe("Observation");

        var cardinalityChecks = schema.Checks.OfType<CardinalityCheck>().ToList();
        cardinalityChecks.ShouldNotBeEmpty();
    }

    #endregion

    #region Cardinality Checks

    [Fact]
    public void GivenPatientStructure_WhenBuildingSchema_ThenCreatesCardinalityChecks()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Patient");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var cardinalityChecks = schema.Checks.OfType<CardinalityCheck>().ToList();
        cardinalityChecks.ShouldNotBeEmpty();

        // Every element should have a cardinality check
        var elements = typeDefinition!.Children;
        cardinalityChecks.Count.ShouldBe(elements.Count);
    }

    [Fact]
    public void GivenElementWithMinZero_WhenBuildingSchema_ThenCreatesCardinalityCheckWithMinZero()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Patient");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

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

        optionalElements.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenRequiredElement_WhenBuildingSchema_ThenCreatesCardinalityCheckWithMinOne()
    {
        // Arrange
        // Use Observation which has required elements (code, status)
        var typeDefinition = _schema.GetTypeDefinition("Observation");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

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
        requiredElements.ShouldNotBeEmpty();
    }

    #endregion

    #region Type Checks

    [Fact]
    public void GivenPatientStructure_WhenBuildingSchema_ThenCreatesTypeChecks()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Patient");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var typeChecks = schema.Checks.OfType<TypeCheck>().ToList();
        typeChecks.ShouldNotBeEmpty();

        // Should have type checks for primitive fields
        // Patient has primitive fields like 'active' (boolean), 'birthDate' (date), etc.
        var hasPrimitiveTypeCheck = typeChecks.Any(c =>
        {
            var typeField = typeof(TypeCheck).GetField("_expectedType",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return typeField != null && typeField.GetValue(c) is string type &&
                   (type == "boolean" || type == "date");
        });
        hasPrimitiveTypeCheck.ShouldBeTrue();

        // Note: "id" element validation is handled by IdFormatCheck (universal check)
        // not by TypeCheck, because the "id" element is inherited from Resource base type
        // and may not be included in the schema's GetElements() collection
    }

    [Fact]
    public void GivenPrimitiveTypes_WhenBuildingSchema_ThenOnlyIncludesPrimitiveTypeChecks()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Patient");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

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

            primitiveTypes.ShouldContain(expectedType);
        }
    }

    #endregion

    #region Reference Format Checks

    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenCreatesReferenceChecks()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Observation");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var referenceChecks = schema.Checks.OfType<ReferenceFormatCheck>().ToList();

        // Observation has reference fields like 'subject', 'performer', etc.
        referenceChecks.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenPatientStructure_WhenBuildingSchema_ThenCreatesReferenceChecksForReferenceFields()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Patient");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var referenceChecks = schema.Checks.OfType<ReferenceFormatCheck>().ToList();

        // Patient has reference fields like 'managingOrganization', 'generalPractitioner', etc.
        referenceChecks.ShouldNotBeEmpty();
    }

    #endregion

    #region Coding Structure Checks

    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenCreatesCodingChecks()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Observation");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var codingChecks = schema.Checks.OfType<CodingStructureCheck>().ToList();

        // Observation has CodeableConcept fields like 'code', 'category', etc.
        codingChecks.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenAllergyIntoleranceStructure_WhenBuildingSchema_ThenCreatesCodingChecksForCodeableConceptFields()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("AllergyIntolerance");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var codingChecks = schema.Checks.OfType<CodingStructureCheck>().ToList();

        // AllergyIntolerance has multiple CodeableConcept fields
        codingChecks.ShouldNotBeEmpty();
    }

    #endregion

    #region Error Handling

    [Fact]
    public void GivenNullTypeDefinition_WhenBuildingSchema_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new StructureDefinitionSchemaBuilder();

        // Act & Assert
        var act = () => builder.BuildSchema(null!, _schema);
        Should.Throw<ArgumentNullException>(act)
            .ParamName.ShouldBe("typeDefinition");
    }

    [Fact]
    public void GivenNullSchema_WhenBuildingSchema_ThenThrowsArgumentNullException()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Patient");
        var builder = new StructureDefinitionSchemaBuilder();

        // Act & Assert
        var act = () => builder.BuildSchema(typeDefinition!, null!);
        Should.Throw<ArgumentNullException>(act)
            .ParamName.ShouldBe("schema");
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
            .Select(rt => _builder.BuildSchema(_schema.GetTypeDefinition(rt)!, _schema))
            .ToList();

        // Assert
        schemas.Count.ShouldBe(3);
        schemas.Select(s => s.CanonicalUrl).Distinct().ToList().Count.ShouldBe(3);
        schemas.ShouldContain(s => s.CanonicalUrl == "http://hl7.org/fhir/StructureDefinition/Patient");
        schemas.ShouldContain(s => s.CanonicalUrl == "http://hl7.org/fhir/StructureDefinition/Observation");
        schemas.ShouldContain(s => s.CanonicalUrl == "http://hl7.org/fhir/StructureDefinition/Condition");
    }

    [Fact]
    public void GivenComplexResourceType_WhenBuildingSchema_ThenCreatesAllCheckTypes()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Observation");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert - Observation should have all types of checks
        schema.Checks.ShouldNotBeEmpty();
        schema.Checks.OfType<CardinalityCheck>().ShouldNotBeEmpty();
        schema.Checks.OfType<TypeCheck>().ShouldNotBeEmpty();
        schema.Checks.OfType<ReferenceFormatCheck>().ShouldNotBeEmpty();
        schema.Checks.OfType<CodingStructureCheck>().ShouldNotBeEmpty();
    }

    #endregion

    #region Nested Type Resolution

    [Fact]
    public void GivenTimingStructure_WhenBuildingSchema_ThenResolvesNestedRepeatTypeCorrectly()
    {
        // Arrange
        var typeDefinition = _schema.GetTypeDefinition("Timing");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var nestedChecks = schema.Checks.OfType<NestedComplexTypeCheck>().ToList();

        // Timing has a 'repeat' element which is of type Element in the definition
        // but should be resolved to Timing.Repeat backbone element
        var repeatCheck = nestedChecks.FirstOrDefault(c => c.ElementName == "repeat");

        repeatCheck.ShouldNotBeNull("Should find a NestedComplexTypeCheck for 'repeat'");

        // The nested schema for 'repeat' should have its own checks
        repeatCheck.NestedSchema.Checks.ShouldNotBeEmpty();

        // Verify we have cardinality checks for fields of Timing.Repeat
        // Timing.Repeat children: bounds[x], count, countMax, duration, durationMax, durationUnit, frequency, etc.
        // Generic Element only has extension, id - so if resolved correctly, we should have many more checks
        var cardinalityChecks = repeatCheck.NestedSchema.Checks.OfType<CardinalityCheck>().ToList();
        cardinalityChecks.ShouldNotBeEmpty();

        // Timing.Repeat has ~10 elements, generic Element only has 2
        cardinalityChecks.Count.ShouldBeGreaterThan(5, "Should have checks for Timing.Repeat properties, not just Element");
    }

    #endregion
}
