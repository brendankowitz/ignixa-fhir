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

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenDoesNotCreateNestedComplexTypeCheckForChoiceElements()
    {
        // Arrange - Bug #210-2: Choice elements (value[x], effective[x]) should NOT get NestedComplexTypeCheck
        // ExtractNestedTypeChecks resolves GetTypeName for choice elements using Types[0].Code (e.g., "Quantity"),
        // then creates a NestedComplexTypeCheck with the wrong type schema applied to all value[x] variants.
        // Choice elements should be validated by ChoiceElementCheck instead.
        var typeDefinition = _schema.GetTypeDefinition("Observation");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var nestedChecks = schema.Checks.OfType<NestedComplexTypeCheck>().ToList();
        var choiceChecks = schema.Checks.OfType<ChoiceElementCheck>().ToList();

        // Observation has choice elements: value[x], effective[x], component.value[x]
        // These should have ChoiceElementCheck, NOT NestedComplexTypeCheck
        choiceChecks.ShouldNotBeEmpty("Observation should have ChoiceElementChecks for value[x] and effective[x]");

        // Verify no NestedComplexTypeCheck exists for choice element base names
        // The choice element base names (without [x]) should not appear as NestedComplexTypeCheck targets
        var choiceElementBaseNames = new[] { "value", "effective" };
        foreach (var baseName in choiceElementBaseNames)
        {
            var wronglyNestedCheck = nestedChecks.FirstOrDefault(c => c.ElementName == baseName);
            wronglyNestedCheck.ShouldBeNull(
                $"Choice element '{baseName}[x]' should NOT have a NestedComplexTypeCheck - " +
                "it should be validated by ChoiceElementCheck instead");
        }
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenMedicationRequestStructure_WhenBuildingSchema_ThenDoesNotCreateNestedComplexTypeCheckForMedicationChoice()
    {
        // Arrange - MedicationRequest has medication[x] which can be CodeableConcept or Reference
        // NestedComplexTypeCheck should NOT be created for this choice element
        var typeDefinition = _schema.GetTypeDefinition("MedicationRequest");

        // Act
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Assert
        var nestedChecks = schema.Checks.OfType<NestedComplexTypeCheck>().ToList();
        var wrongCheck = nestedChecks.FirstOrDefault(c => c.ElementName == "medication");
        wrongCheck.ShouldBeNull(
            "Choice element 'medication[x]' should NOT have NestedComplexTypeCheck");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenNoTypeCheckForChoiceElements()
    {
        // Bug #210-2: Choice elements should NOT get TypeCheck because their concrete type
        // varies at runtime (e.g., effective[x] can be dateTime, Period, Timing, or Instant)
        var typeDefinition = _schema.GetTypeDefinition("Observation");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var typeChecks = schema.Checks.OfType<TypeCheck>().ToList();
        foreach (var check in typeChecks)
        {
            var elementNameField = typeof(TypeCheck).GetField("_elementName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var elementName = (string)elementNameField!.GetValue(check)!;

            var choiceBaseNames = new[] { "value", "effective" };
            choiceBaseNames.ShouldNotContain(elementName,
                $"TypeCheck should not be created for choice element '{elementName}' - " +
                "concrete type depends on runtime data");
        }
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenNoNestedComplexTypeCheckForChoiceElements()
    {
        // Choice elements should NOT get NestedComplexTypeCheck
        var typeDefinition = _schema.GetTypeDefinition("Observation");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var nestedChecks = schema.Checks.OfType<NestedComplexTypeCheck>().ToList();
        foreach (var check in nestedChecks)
        {
            var choiceBaseNames = new[] { "value", "effective" };
            choiceBaseNames.ShouldNotContain(check.ElementName,
                $"NestedComplexTypeCheck exists for choice element base name '{check.ElementName}'");
        }
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationComponentStructure_WhenBuildingSchema_ThenNoTypeOrNestedCheckForValueChoiceElement()
    {
        // Observation.Component has value[x] which is a choice element
        // It should NOT get TypeCheck or NestedComplexTypeCheck
        var typeDefinition = _schema.GetTypeDefinition("Observation.Component");
        typeDefinition.ShouldNotBeNull("Observation.Component should exist in schema");

        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // No NestedComplexTypeCheck for value
        var nestedChecks = schema.Checks.OfType<NestedComplexTypeCheck>().ToList();
        var wrongNestedCheck = nestedChecks.FirstOrDefault(c => c.ElementName == "value");
        wrongNestedCheck.ShouldBeNull(
            "Observation.Component.value[x] should NOT have a NestedComplexTypeCheck");

        // No TypeCheck for value (defaultTypeName is Quantity, a non-primitive, so this shouldn't
        // normally happen - but verify the pattern is correct)
        var typeChecks = schema.Checks.OfType<TypeCheck>().ToList();
        foreach (var check in typeChecks)
        {
            var elementNameField = typeof(TypeCheck).GetField("_elementName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var elementName = (string)elementNameField!.GetValue(check)!;
            elementName.ShouldNotBe("value",
                "TypeCheck should not be created for choice element 'value'");
        }
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenExtensionStructure_WhenBuildingSchema_ThenNoTypeCheckForValueChoiceElement()
    {
        // Extension has value[x] with many types including primitives (base64Binary, boolean, etc.)
        // and complex types (Coding, Address, etc.)
        // TypeCheck should NOT be created for choice elements even when defaultTypeName is primitive
        var typeDefinition = _schema.GetTypeDefinition("Extension");
        typeDefinition.ShouldNotBeNull("Extension should exist in schema");

        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var typeChecks = schema.Checks.OfType<TypeCheck>().ToList();
        foreach (var check in typeChecks)
        {
            var elementNameField = typeof(TypeCheck).GetField("_elementName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var elementName = (string)elementNameField!.GetValue(check)!;
            elementName.ShouldNotBe("value",
                "TypeCheck should not be created for choice element 'value' in Extension - " +
                "value[x] can be valueCoding, valueAddress, etc. (not just primitive types)");
        }
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationStructure_WhenBuildingSchema_ThenChoiceElementBasesAreInUnknownPropertyCheck()
    {
        // Verify that UnknownPropertyCheck recognizes choice element property names
        var typeDefinition = _schema.GetTypeDefinition("Observation");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var unknownPropertyChecks = schema.Checks.OfType<UnknownPropertyCheck>().ToList();
        unknownPropertyChecks.ShouldNotBeEmpty();

        // Verify the UnknownPropertyCheck has choice element bases
        var check = unknownPropertyChecks[0];
        var choiceBasesField = typeof(UnknownPropertyCheck).GetField("_choiceElementBases",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        choiceBasesField.ShouldNotBeNull();

        var choiceBases = (HashSet<string>)choiceBasesField.GetValue(check)!;
        choiceBases.ShouldContain("value");
        choiceBases.ShouldContain("effective");
    }
}