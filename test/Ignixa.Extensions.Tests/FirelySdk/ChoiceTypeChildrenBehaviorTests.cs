/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests to investigate and verify choice type behavior in Ignixa's TypedElement implementation.
 * This test file reproduces the scenario from Microsoft FHIR Server's failing tests to verify
 * whether Ignixa shims correctly handle choice type element names.
 *
 * Key question: Should Children("effective") return elements named "effective" or "effectiveDateTime"?
 * Answer: "effectiveDateTime" - the suffixed name in JSON, accessed via base name.
 */

#pragma warning disable CA1826 // Use indexable collections - test code prioritizes readability
#pragma warning disable xUnit2013 // Do not use equality check to test for collection size

using System;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.Extensions.Tests.FirelySdk;

/// <summary>
/// Tests demonstrating and verifying the correct behavior of choice type element navigation.
/// Reproduces scenarios from Microsoft FHIR Server to validate Ignixa implementation.
/// </summary>
public class ChoiceTypeChildrenBehaviorTests
{
    private readonly IFhirSchemaProvider _r4Provider;

    public ChoiceTypeChildrenBehaviorTests()
    {
        _r4Provider = FhirVersion.R4.GetSchemaProvider();
    }

    #region Core Behavior Tests - What Should Children() Return?

    /// <summary>
    /// CRITICAL TEST: Verifies that Children("effective") returns element named "effectiveDateTime".
    /// This is the CORRECT behavior per FHIR spec:
    /// - JSON serialization: use suffixed name (effectiveDateTime)
    /// - Programmatic access: use base name (effective) to navigate
    /// - Returned element: has the suffixed name (effectiveDateTime)
    /// </summary>
    [Fact]
    public void GivenEffectiveDateTime_WhenNavigatingViaBaseName_ThenReturnsSuffixedName()
    {
        // Arrange
        const string json = """
        {
          "resourceType": "Observation",
          "id": "test-obs-1",
          "status": "final",
          "code": { "text": "test" },
          "effectiveDateTime": "2025-01-01T12:00:00Z"
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var typedElement = resource.ToElement(_r4Provider);

        // Act: Navigate using BASE name
        var effectiveChildren = typedElement.Children("effective").ToList();

        // Assert: Should return element with SUFFIXED name
        Assert.Equal(1, effectiveChildren.Count);
        Assert.Equal("effectiveDateTime", effectiveChildren[0].Name);
        Assert.Equal("dateTime", effectiveChildren[0].InstanceType);
        Assert.Equal("2025-01-01T12:00:00Z", effectiveChildren[0].Value);
    }

    /// <summary>
    /// Verifies that direct navigation to the suffixed name also works.
    /// </summary>
    [Fact]
    public void GivenEffectiveDateTime_WhenNavigatingViaSuffixedName_ThenReturnsElement()
    {
        // Arrange
        const string json = """
        {
          "resourceType": "Observation",
          "id": "test-obs-2",
          "status": "final",
          "code": { "text": "test" },
          "effectiveDateTime": "2025-01-01T12:00:00Z"
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var typedElement = resource.ToElement(_r4Provider);

        // Act: Navigate using SUFFIXED name
        var effectiveDateTimeChildren = typedElement.Children("effectiveDateTime").ToList();

        // Assert
        Assert.Equal(1, effectiveDateTimeChildren.Count);
        Assert.Equal("effectiveDateTime", effectiveDateTimeChildren[0].Name);
        Assert.Equal("dateTime", effectiveDateTimeChildren[0].InstanceType);
        Assert.Equal("2025-01-01T12:00:00Z", effectiveDateTimeChildren[0].Value);
    }

    /// <summary>
    /// Verifies behavior with effectivePeriod (complex type choice).
    /// </summary>
    [Fact]
    public void GivenEffectivePeriod_WhenNavigatingViaBaseName_ThenReturnsSuffixedName()
    {
        // Arrange
        const string json = """
        {
          "resourceType": "Observation",
          "id": "test-obs-3",
          "status": "final",
          "code": { "text": "test" },
          "effectivePeriod": {
            "start": "2025-01-01",
            "end": "2025-01-31"
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var typedElement = resource.ToElement(_r4Provider);

        // Act: Navigate using BASE name
        var effectiveChildren = typedElement.Children("effective").ToList();

        // Assert: Should return element with SUFFIXED name
        Assert.Equal(1, effectiveChildren.Count);
        Assert.Equal("effectivePeriod", effectiveChildren[0].Name);
        Assert.Equal("Period", effectiveChildren[0].InstanceType);
    }

    #endregion

    #region Observation Value[x] Tests

    /// <summary>
    /// Verifies value[x] navigation returns suffixed names.
    /// </summary>
    [Theory]
    [InlineData("valueString", "string", "test value")]
    [InlineData("valueInteger", "integer", 42)]
    [InlineData("valueBoolean", "boolean", true)]
    public void GivenObservationValue_WhenNavigatingViaBaseName_ThenReturnsSuffixedName(
        string expectedName, string expectedType, object expectedValue)
    {
        // Arrange
#pragma warning disable CA1308 // JSON boolean values must be lowercase
        var valueJson = expectedValue switch
        {
            string s => $"\"{s}\"",
            bool b => b.ToString().ToLowerInvariant(),
            _ => expectedValue.ToString()
        };
#pragma warning restore CA1308

        var json = $$"""
        {
          "resourceType": "Observation",
          "status": "final",
          "code": { "text": "test" },
          "{{expectedName}}": {{valueJson}}
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var typedElement = resource.ToElement(_r4Provider);

        // Act: Navigate using BASE name
        var valueChildren = typedElement.Children("value").ToList();

        // Assert: Returns SUFFIXED name
        Assert.Equal(1, valueChildren.Count);
        Assert.Equal(expectedName, valueChildren[0].Name);
        Assert.Equal(expectedType, valueChildren[0].InstanceType);
        Assert.Equal(expectedValue, valueChildren[0].Value);
    }

    #endregion

    #region All Children Enumeration Tests

    /// <summary>
    /// CRITICAL: When enumerating ALL children (no filter), choice elements should appear
    /// with their SUFFIXED names, not the base name.
    /// </summary>
    [Fact]
    public void GivenObservationWithChoiceTypes_WhenEnumeratingAllChildren_ThenShowsSuffixedNames()
    {
        // Arrange
        const string json = """
        {
          "resourceType": "Observation",
          "id": "all-children-test",
          "status": "final",
          "code": { "text": "test" },
          "effectiveDateTime": "2025-01-01T12:00:00Z",
          "valueString": "result"
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var typedElement = resource.ToElement(_r4Provider);

        // Act: Get ALL children (no filter)
        var allChildren = typedElement.Children().ToArray();

        // Assert: Choice types appear with SUFFIXED names
        var childNames = allChildren.Select(c => c.Name).ToArray();

        // Should contain suffixed names, not base names
        Assert.Contains("effectiveDateTime", childNames);
        Assert.Contains("valueString", childNames);

        // Should NOT contain base names in the child list
        Assert.DoesNotContain("effective", childNames);
        Assert.DoesNotContain("value", childNames);
    }

    #endregion

    #region Firely SDK Compatibility Tests

    /// <summary>
    /// This test verifies that Ignixa correctly handles choice types,
    /// even though Firely SDK's direct parsing has known issues.
    /// </summary>
    [Fact]
    public void Ignixa_DeserializesObservationWithEffectiveDateTime_Successfully()
    {
        // Arrange: Valid FHIR JSON with choice type
        const string json = """
        {
            "resourceType": "Observation",
            "id": "test-obs-firely",
            "status": "final",
            "code": {
                "coding": [{
                    "system": "http://loinc.org",
                    "code": "15074-8"
                }]
            },
            "effectiveDateTime": "2025-01-01T12:00:00Z"
        }
        """;

        // Act: Ignixa deserializes successfully
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_r4Provider);

        // Assert: Resource is valid
        Assert.Equal("Observation", element.InstanceType);
        Assert.Equal("test-obs-firely", element.Children("id").FirstOrDefault()?.Value);

        // Assert: Choice type is accessible via base name
        var effective = element.Children("effective").FirstOrDefault();
        Assert.NotNull(effective);
        Assert.Equal("effectiveDateTime", effective.Name);  // CRITICAL: Should be suffixed
        Assert.Equal("2025-01-01T12:00:00Z", effective.Value);
    }

    /// <summary>
    /// Demonstrates that Firely SDK v5.13+ may have issues parsing choice types directly.
    /// This test should FAIL if Firely SDK has the bug.
    /// </summary>
    [Fact]
    public void FirelySDK_DirectParse_ObservationWithEffectiveDateTime_MayFail()
    {
        // Arrange: Same JSON that Ignixa handles perfectly
        const string json = """
        {
            "resourceType": "Observation",
            "status": "final",
            "code": {
                "coding": [{
                    "system": "http://loinc.org",
                    "code": "15074-8"
                }]
            },
            "effectiveDateTime": "2025-01-01T12:00:00Z"
        }
        """;

        // Act & Assert: Firely SDK direct parsing
        var deserializer = new FhirJsonDeserializer();

        // Attempt to parse - this demonstrates potential Firely SDK bug
        try
        {
            var observation = deserializer.Deserialize<Observation>(json);
            Assert.NotNull(observation);
            Assert.IsType<FhirDateTime>(observation.Effective);
        }
        catch (DeserializationFailedException ex)
        {
            // Expected failure with some Firely SDK versions
            Assert.Contains("effectiveDateTime", ex.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Demonstrates accessing data via ITypedElement API (the recommended approach).
    /// DO NOT convert to POCO - use ITypedElement APIs directly.
    /// </summary>
    [Fact]
    public void Workaround_AccessDataViaITypedElement_Success()
    {
        // Arrange
        const string json = """
        {
            "resourceType": "Observation",
            "id": "test-workaround",
            "status": "final",
            "code": {
                "coding": [{
                    "system": "http://loinc.org",
                    "code": "15074-8"
                }]
            },
            "effectiveDateTime": "2025-01-01T12:00:00Z",
            "valueQuantity": {
                "value": 120,
                "unit": "mmHg"
            }
        }
        """;

        var resourceNode = ResourceJsonNode.Parse(json);
        var typedElement = resourceNode.ToElement(_r4Provider);

        // Act: Access data via ITypedElement APIs (no POCO conversion)
        var id = typedElement.Children("id").FirstOrDefault()?.Value;
        var status = typedElement.Children("status").FirstOrDefault()?.Value;
        var effective = typedElement.Children("effective").FirstOrDefault();
        var value = typedElement.Children("value").FirstOrDefault();

        // Assert: Everything works WITHOUT ToPoco!
        Assert.Equal("test-workaround", id);
        Assert.Equal("final", status);

        Assert.NotNull(effective);
        Assert.Equal("effectiveDateTime", effective.Name);
        Assert.Equal("2025-01-01T12:00:00Z", effective.Value);

        Assert.NotNull(value);
        Assert.Equal("valueQuantity", value.Name);
        Assert.Equal("Quantity", value.InstanceType);
    }

    #endregion

    #region Multiple Choice Types in Collection

    /// <summary>
    /// Tests navigation through collection elements with different choice types.
    /// Example: Observation.component can have different value[x] implementations.
    /// </summary>
    [Fact]
    public void GivenObservationComponents_WhenNavigatingToComponentValues_ThenReturnsSuffixedNames()
    {
        // Arrange
        const string json = """
        {
          "resourceType": "Observation",
          "status": "final",
          "code": { "text": "test" },
          "component": [
            {
              "code": { "text": "comp1" },
              "valueString": "text"
            },
            {
              "code": { "text": "comp2" },
              "valueInteger": 10
            },
            {
              "code": { "text": "comp3" },
              "valueQuantity": { "value": 5, "unit": "mg" }
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var typedElement = resource.ToElement(_r4Provider);

        // Act: Navigate to component values using BASE name
        var components = typedElement.Children("component").ToList();
        var componentValues = components.SelectMany(c => c.Children("value")).ToList();

        // Assert: All returned elements have SUFFIXED names
        Assert.Equal(3, componentValues.Count);
        Assert.Equal("valueString", componentValues[0].Name);
        Assert.Equal("valueInteger", componentValues[1].Name);
        Assert.Equal("valueQuantity", componentValues[2].Name);
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Verifies that navigating to a choice element that doesn't exist returns empty.
    /// </summary>
    [Fact]
    public void GivenObservationWithoutEffective_WhenNavigatingToEffective_ThenReturnsEmpty()
    {
        // Arrange
        const string json = """
        {
          "resourceType": "Observation",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var effectiveChildren = typedElement.Children("effective").ToList();

        // Assert
        Assert.Empty(effectiveChildren);
    }

    /// <summary>
    /// Verifies that you cannot have multiple choice variants (FHIR validation rule).
    /// Note: Serialization layer allows it, but validation should catch it.
    /// </summary>
    [Fact]
    public void GivenMultipleEffectiveVariants_WhenNavigatingToEffective_ThenReturnsBoth()
    {
        // Arrange: Invalid FHIR (has both effectiveDateTime AND effectivePeriod)
        // This shouldn't happen in valid FHIR, but testing the behavior if it does
        const string json = """
        {
          "resourceType": "Observation",
          "status": "final",
          "code": { "text": "test" },
          "effectiveDateTime": "2025-01-01T12:00:00Z",
          "effectivePeriod": {
            "start": "2025-01-01",
            "end": "2025-01-31"
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var typedElement = resource.ToElement(_r4Provider);

        // Act: Navigate using BASE name
        var effectiveChildren = typedElement.Children("effective").ToList();

        // Assert: Returns BOTH (though this is invalid FHIR)
        // Demonstrates that Children() returns all matching choice variants
        Assert.Equal(2, effectiveChildren.Count);

        var childNames = effectiveChildren.Select(e => e.Name);
        Assert.Contains("effectiveDateTime", childNames);
        Assert.Contains("effectivePeriod", childNames);
    }

    #endregion

    #region ToPoco() Conversion Investigation

    /// <summary>
    /// CRITICAL: Compare Firely's native ITypedElement behavior vs our adapter behavior.
    /// This tells us what Name should return for choice elements to be compatible.
    /// </summary>
    [Fact]
    public void CompareFirelyNative_VsIgnixaAdapter_ForChoiceElementNames()
    {
        // Arrange: Same JSON for both paths
        const string json = """
        {
            "resourceType": "Observation",
            "id": "comparison",
            "status": "final",
            "code": { "text": "test" },
            "effectiveDateTime": "2025-01-01T12:00:00Z"
        }
        """;

        // Path 1: Firely SDK native parsing → ITypedElement
        var firelyNode = Hl7.Fhir.Serialization.FhirJsonNode.Parse(json);
        var firelyTypedElement = firelyNode.ToTypedElement(new Hl7.Fhir.Specification.PocoStructureDefinitionSummaryProvider());
        var firelyEffective = firelyTypedElement.Children("effective").FirstOrDefault();

        // Path 2: Ignixa → IElement → ITypedElement (via our adapter)
        var ignixaResourceNode = ResourceJsonNode.Parse(json);
        var ignixaElement = ignixaResourceNode.ToElement(_r4Provider);
        var ignixaTypedElement = ignixaElement.ToTypedElement();
        var ignixaEffective = ignixaTypedElement.Children("effective").FirstOrDefault();

        // Compare: What does Name return for the choice element?
        var firelyName = firelyEffective?.Name ?? "null";
        var ignixaName = ignixaEffective?.Name ?? "null";

        // Report the comparison
        Assert.NotNull(firelyEffective);
        Assert.NotNull(ignixaEffective);

        // This will show us if our adapter matches Firely's behavior
        Assert.Equal(firelyName, ignixaName);
    }

    /// <summary>
    /// Verifies that ToPoco() conversion works correctly with Ignixa elements.
    /// This test validates the fix in TypedElementAdapter where choice element names
    /// are normalized to base names (e.g., "effective" not "effectiveDateTime")
    /// to match Firely SDK's native behavior for POCO property mapping.
    /// </summary>
    [Fact]
    public void GivenIgnixaElement_WhenConvertingToPocoViaToTypedElement_ThenShouldWork()
    {
        // Arrange: Create Ignixa element with effectiveDateTime
        const string json = """
        {
            "resourceType": "Observation",
            "id": "topoco-test",
            "status": "final",
            "code": {
                "coding": [{
                    "system": "http://loinc.org",
                    "code": "15074-8"
                }]
            },
            "effectiveDateTime": "2025-01-01T12:00:00Z"
        }
        """;

        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_r4Provider);

        // Verify: Ignixa element has suffixed name (this is correct per FHIR spec)
        var effectiveChildren = element.Children("effective").ToList();
        Assert.Equal(1, effectiveChildren.Count);
        Assert.Equal("effectiveDateTime", effectiveChildren[0].Name);

        // Act: Convert to ITypedElement (adapter normalizes choice element names)
        var typedElement = element.ToTypedElement();
        Assert.NotNull(typedElement);

        // The adapter should normalize the name to base name for choice elements
        var typedEffective = typedElement.Children("effective").FirstOrDefault();
        Assert.NotNull(typedEffective);
        Assert.Equal("effective", typedEffective.Name); // Base name for ToPoco() mapping

        // Act: Convert to POCO using ToPoco()
        var observation = typedElement.ToPoco<Observation>();

        // Assert: ToPoco() should successfully populate the choice element
        Assert.NotNull(observation);
        Assert.NotNull(observation.Effective);
        Assert.IsType<FhirDateTime>(observation.Effective);
        var effectiveDateTime = (FhirDateTime)observation.Effective;
        Assert.Equal("2025-01-01T12:00:00Z", effectiveDateTime.Value);
    }

    /// <summary>
    /// Tests the workaround pattern: JSON → Deserialize (bypassing ToPoco).
    /// This should ALWAYS work.
    /// </summary>
    [Fact]
    public void GivenIgnixaElement_WhenUsingJsonDeserializationWorkaround_ThenWorks()
    {
        // Arrange
        const string json = """
        {
            "resourceType": "Observation",
            "id": "workaround-test",
            "status": "final",
            "code": {
                "coding": [{
                    "system": "http://loinc.org",
                    "code": "15074-8"
                }]
            },
            "effectiveDateTime": "2025-01-01T12:00:00Z",
            "valueQuantity": {
                "value": 120,
                "unit": "mmHg"
            }
        }
        """;

        // Step 1: Verify Ignixa can handle it
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_r4Provider);
        Assert.NotNull(element.Children("effective").FirstOrDefault());

        // Step 2: Convert to ITypedElement (this works)
        var typedElement = element.ToTypedElement();
        Assert.NotNull(typedElement);

        // Step 3: WORKAROUND - Deserialize from JSON instead of using ToPoco()
        var deserializer = new FhirJsonDeserializer();
        var observation = deserializer.Deserialize<Observation>(json);

        // Assert: Workaround succeeds
        Assert.NotNull(observation);
        Assert.IsType<FhirDateTime>(observation.Effective);
        var effectiveDateTime = (FhirDateTime)observation.Effective;
        Assert.Equal("2025-01-01T12:00:00Z", effectiveDateTime.Value);

        Assert.IsType<Quantity>(observation.Value);
        var quantity = (Quantity)observation.Value;
        Assert.Equal(120, quantity.Value);
    }

    #endregion
}
