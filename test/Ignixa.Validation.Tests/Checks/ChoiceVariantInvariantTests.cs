// <copyright file="ChoiceVariantInvariantTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// A datatype's invariants do not become optional because the element carrying it is polymorphic.
/// </summary>
/// <remarks>
/// <para>
/// The validator descends into nested datatypes two different ways: <c>NestedComplexTypeCheck</c> for
/// a monomorphic element (<c>Dosage.timing</c>), and <c>ChoiceVariantNestedCheck</c> for a concrete
/// <c>value[x]</c> variant (<c>ServiceRequest.occurrenceTiming</c>). Both reach the same
/// <c>Timing</c> schema, so both must reach the same verdict; the choice path used to run the subtree
/// at Spec depth, which silently discarded every FHIRPath invariant below it.
/// </para>
/// <para>
/// Each test therefore pairs a violation reached through a choice element with the identical
/// violation reached through a plain one, and asserts they agree. Asserting only the choice path
/// would still pass if the whole traversal regressed to reporting nothing.
/// </para>
/// </remarks>
public class ChoiceVariantInvariantTests
{
    private readonly ISchema _schema = TestSchemaProvider.GetR4Schema();
    private readonly IValidationSchemaResolver _resolver;

    public ChoiceVariantInvariantTests()
    {
        _resolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(_schema));
    }

    /// <summary>
    /// The reported symptom: <c>Timing.repeat</c> carrying <c>when: ['C']</c> alongside an
    /// <c>offset</c> is exactly what tim-9 forbids, and a single <c>when</c> keeps the constraint's
    /// <c>in</c> operator singleton so the engine really does evaluate it.
    /// </summary>
    [Fact]
    public void GivenTim9ViolationUnderAChoiceElement_WhenValidatingAtFull_ThenTheResourceFails()
    {
        // Arrange
        var serviceRequest = ServiceRequestWithOccurrence("""
            "occurrenceTiming": {
                "repeat": { "when": ["C"], "offset": 30 }
            }
        """);

        // Act
        var result = Validate(serviceRequest, "ServiceRequest", ValidationDepth.Full);

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(
            i => i.Code == "tim-9" && i.Severity == IssueSeverity.Error,
            Describe(result));
    }

    /// <summary>
    /// The same violation through a monomorphic <c>Dosage.timing</c>. This path already worked, and
    /// pinning it keeps the pair honest: the fix had to raise the choice path to this, not lower this
    /// to the choice path.
    /// </summary>
    [Fact]
    public void GivenTheSameTim9ViolationUnderAPlainElement_WhenValidatingAtFull_ThenTheResourceFailsIdentically()
    {
        // Arrange
        var medicationRequest = """
        {
            "resourceType": "MedicationRequest",
            "id": "control",
            "status": "active",
            "intent": "order",
            "subject": { "reference": "Patient/example" },
            "medicationCodeableConcept": { "text": "aspirin" },
            "dosageInstruction": [{ "timing": { "repeat": { "when": ["C"], "offset": 30 } } }]
        }
        """;

        // Act
        var result = Validate(medicationRequest, "MedicationRequest", ValidationDepth.Full);

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(
            i => i.Code == "tim-9" && i.Severity == IssueSeverity.Error,
            Describe(result));
    }

    /// <summary>
    /// Period / per-1. Reached only through <c>occurrence[x]</c> here, and through the plain
    /// <c>Patient.name.period</c> in the paired test below.
    /// </summary>
    [Fact]
    public void GivenAnInvertedPeriodUnderAChoiceElement_WhenValidatingAtFull_ThenPer1Fails()
    {
        // Arrange
        var serviceRequest = ServiceRequestWithOccurrence("""
            "occurrencePeriod": { "start": "2026-06-01", "end": "2026-01-01" }
        """);

        // Act
        var result = Validate(serviceRequest, "ServiceRequest", ValidationDepth.Full);

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(
            i => i.Code == "per-1" && i.Severity == IssueSeverity.Error,
            Describe(result));
    }

    [Fact]
    public void GivenAnInvertedPeriodUnderAPlainElement_WhenValidatingAtFull_ThenPer1FailsIdentically()
    {
        // Arrange
        var patient = """
        {
            "resourceType": "Patient",
            "id": "control",
            "name": [{ "family": "Doe", "period": { "start": "2026-06-01", "end": "2026-01-01" } }]
        }
        """;

        // Act
        var result = Validate(patient, "Patient", ValidationDepth.Full);

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(
            i => i.Code == "per-1" && i.Severity == IssueSeverity.Error,
            Describe(result));
    }

    /// <summary>
    /// DataRequirement / drq-1 (<c>path.exists() xor searchParam.exists()</c>), declared on
    /// <c>DataRequirement.codeFilter</c>. Two levels below the choice variant, so it also proves the
    /// restored depth propagates through the variant's own nested elements rather than stopping at
    /// the variant root.
    /// </summary>
    [Fact]
    public void GivenACodeFilterWithBothPathAndSearchParamUnderAChoiceElement_WhenValidatingAtFull_ThenDrq1Fails()
    {
        // Arrange
        var parameters = """
        {
            "resourceType": "Parameters",
            "id": "drq",
            "parameter": [{
                "name": "requirement",
                "valueDataRequirement": {
                    "type": "Observation",
                    "codeFilter": [{ "path": "code", "searchParam": "code" }]
                }
            }]
        }
        """;

        // Act
        var result = Validate(parameters, "Parameters", ValidationDepth.Full);

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(
            i => i.Code == "drq-1" && i.Severity == IssueSeverity.Error,
            Describe(result));
    }

    /// <summary>
    /// Timing / tim-2, a different invariant on the same datatype through a different choice element,
    /// so the tim-9 result cannot be an artefact of one element's wiring.
    /// </summary>
    [Fact]
    public void GivenATimingPeriodWithoutUnitsUnderADifferentChoiceElement_WhenValidatingAtFull_ThenTim2Fails()
    {
        // Arrange
        var carePlan = """
        {
            "resourceType": "CarePlan",
            "id": "tim2",
            "status": "active",
            "intent": "plan",
            "subject": { "reference": "Patient/example" },
            "activity": [{ "detail": { "status": "scheduled", "scheduledTiming": { "repeat": { "period": 3 } } } }]
        }
        """;

        // Act
        var result = Validate(carePlan, "CarePlan", ValidationDepth.Full);

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(
            i => i.Code == "tim-2" && i.Severity == IssueSeverity.Error,
            Describe(result));
    }

    /// <summary>
    /// Negative control. Without it every test above would pass just as happily if the traversal had
    /// been made to fire indiscriminately. The instance exercises the same three datatypes through the
    /// same choice elements, conformantly, and must come back silent.
    /// </summary>
    /// <remarks>
    /// This one carries a narrative, unlike the violation fixtures above. dom-6 ("a resource should
    /// have narrative for robust management") is a real best-practice warning on every DomainResource
    /// root, so an instance without narrative is not silent - it is merely not in error. A negative
    /// control that asserts zero issues has to be genuinely clean, not clean-except-the-ones-we-expect.
    /// </remarks>
    [Fact]
    public void GivenAValidResourceWithDeeplyNestedDatatypes_WhenValidatingAtFull_ThenNoIssuesAreRaised()
    {
        // Arrange
        var serviceRequest = ServiceRequestWithOccurrence("""
            "text": {
                "status": "generated",
                "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">Timed service request</div>"
            },
            "occurrenceTiming": {
                "repeat": {
                    "boundsPeriod": { "start": "2026-01-01", "end": "2026-06-01" },
                    "frequency": 2,
                    "period": 1,
                    "periodUnit": "d",
                    "when": ["MORN"],
                    "offset": 30
                }
            }
        """);

        // Act
        var result = Validate(serviceRequest, "ServiceRequest", ValidationDepth.Full);

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldBeEmpty(Describe(result));
    }

    /// <summary>
    /// Restoring the depth must not change Spec or Compatibility runs: the choice-variant descent is a
    /// profile-tier check and stays inert below Full.
    /// </summary>
    [Theory]
    [InlineData(ValidationDepth.Minimal)]
    [InlineData(ValidationDepth.Spec)]
    [InlineData(ValidationDepth.Compatibility)]
    public void GivenTim9ViolationUnderAChoiceElement_WhenValidatingBelowFull_ThenTheInvariantStaysUnevaluated(
        ValidationDepth depth)
    {
        // Arrange
        var serviceRequest = ServiceRequestWithOccurrence("""
            "occurrenceTiming": {
                "repeat": { "when": ["C"], "offset": 30 }
            }
        """);

        // Act
        var result = Validate(serviceRequest, "ServiceRequest", depth);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "tim-9", Describe(result));
    }

    /// <summary>
    /// R4's tim-9 is ill-formed for a repeating <c>when</c>: it feeds a multi-item collection to
    /// <c>in</c>, which FHIRPath requires the engine to error on. Lighting the choice path must not
    /// turn that refusal into a rejection - it stays a non-failing Warning, the routing
    /// <c>FhirPathInvariantCheck</c> established and the reason the demotion could be removed at all.
    /// </summary>
    [Fact]
    public void GivenAnUnevaluableTim9UnderAChoiceElement_WhenValidatingAtFull_ThenItWarnsWithoutFailing()
    {
        // Arrange — two 'when' codes make the left operand of 'in' a two-item collection.
        var serviceRequest = ServiceRequestWithOccurrence("""
            "occurrenceTiming": {
                "repeat": { "when": ["MORN", "EVE"], "offset": 30 }
            }
        """);

        // Act
        var result = Validate(serviceRequest, "ServiceRequest", ValidationDepth.Full);

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldContain(
            i => i.Code == "tim-9"
                && i.Severity == IssueSeverity.Warning
                && i.Message.Contains("could not be evaluated", StringComparison.Ordinal),
            Describe(result));
    }

    private static string ServiceRequestWithOccurrence(string occurrenceBody) => $$"""
        {
            "resourceType": "ServiceRequest",
            "id": "occurrence-example",
            "status": "active",
            "intent": "order",
            "subject": { "reference": "Patient/example" },
            {{occurrenceBody}}
        }
        """;

    private ValidationResult Validate(string resourceJson, string resourceType, ValidationDepth depth)
    {
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(resourceJson)!);
        var schema = _resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{resourceType}")
            ?? throw new InvalidOperationException($"No schema for {resourceType}");

        return schema.Validate(
            sourceNode.ToElement(_schema),
            new ValidationSettings { Depth = depth },
            new ValidationState());
    }

    private static string Describe(ValidationResult result)
        => result.Issues.Count == 0
            ? "(no issues)"
            : string.Join(" | ", result.Issues.Select(i => $"{i.Severity}:{i.Code}@{i.Path}"));
}
