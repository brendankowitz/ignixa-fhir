// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// End-to-end coverage for the WS1 deterministic sweep items that are wired through the schema
/// builder rather than a single standalone check: the supplemental Period per-1 invariant, the
/// que-12 erratum correction, complex choice-variant recursion (Attachment base64), and relative
/// reference resolution inside a Parameters container.
/// </summary>
public sealed class Ws1DeterministicSweepTests
{
    private static readonly ISchema Schema = new R4CoreSchemaProvider();

    private static readonly IValidationSchemaResolver Resolver =
        new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(Schema));

    private static ValidationResult ValidateFull(string json)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        var schema = Resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{element.InstanceType}")!;
        var state = new ValidationState().EnterRootResource(element);
        return schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Full }, state);
    }

    [Fact]
    public void GivenPeriodWithIndeterminateOrder_WhenValidating_ThenPer1Fires()
    {
        // Arrange - start is a date, end a dateTime; the partial-precision comparison is
        // indeterminate, which per-1 (absent from codegen, injected by the builder) treats as failure.
        var result = ValidateFull("""
        {
            "resourceType": "Encounter",
            "status": "unknown",
            "period": { "start": "2023-06-21", "end": "2023-06-21T06:20:00Z" }
        }
        """);

        // Act / Assert
        result.Issues.ShouldContain(i => i.Code == "per-1" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenPeriodWithStartBeforeEnd_WhenValidating_ThenPer1DoesNotFire()
    {
        // Arrange
        var result = ValidateFull("""
        {
            "resourceType": "Encounter",
            "status": "unknown",
            "period": { "start": "2023-06-21T05:00:00Z", "end": "2023-06-21T06:20:00Z" }
        }
        """);

        // Act / Assert
        result.Issues.ShouldNotContain(i => i.Code == "per-1");
    }

    [Fact]
    public void GivenItemWithTwoEnableWhenAndNoBehavior_WhenValidating_ThenQue12Fires()
    {
        // Arrange - the R4.0.1 que-12 erratum ("> 2") is corrected to ">= 2" so exactly-two fires.
        var result = ValidateFull("""
        {
            "resourceType": "Questionnaire",
            "status": "draft",
            "item": [
                { "linkId": "N0", "type": "integer" },
                { "linkId": "N1", "type": "integer" },
                { "linkId": "N2", "type": "integer",
                  "enableWhen": [
                    { "question": "N0", "operator": "=", "answerInteger": 1 },
                    { "question": "N1", "operator": "=", "answerInteger": 1 }
                  ] }
            ]
        }
        """);

        // Act / Assert
        result.Issues.ShouldContain(i => i.Code == "que-12" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenItemWithTwoEnableWhenAndBehavior_WhenValidating_ThenQue12DoesNotFire()
    {
        // Arrange
        var result = ValidateFull("""
        {
            "resourceType": "Questionnaire",
            "status": "draft",
            "item": [
                { "linkId": "N2", "type": "integer", "enableBehavior": "all",
                  "enableWhen": [
                    { "question": "N0", "operator": "=", "answerInteger": 1 },
                    { "question": "N1", "operator": "=", "answerInteger": 1 }
                  ] }
            ]
        }
        """);

        // Act / Assert
        result.Issues.ShouldNotContain(i => i.Code == "que-12");
    }

    [Fact]
    public void GivenParametersValueAttachmentWithBadBase64_WhenValidating_ThenBase64ErrorFires()
    {
        // Arrange - '...' is not valid base64; the complex choice variant is now walked structurally.
        var result = ValidateFull("""
        {
            "resourceType": "Parameters",
            "parameter": [
                { "name": "attachment",
                  "valueAttachment": { "contentType": "application/octet-stream", "data": "..." } }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Message.Contains("base64", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GivenParametersValueAttachmentWithValidBase64_WhenValidating_ThenNoBase64Error()
    {
        // Arrange - "Zm9v" decodes to "foo".
        var result = ValidateFull("""
        {
            "resourceType": "Parameters",
            "parameter": [
                { "name": "attachment",
                  "valueAttachment": { "contentType": "application/octet-stream", "data": "Zm9v" } }
            ]
        }
        """);

        // Act / Assert
        result.Issues.ShouldNotContain(i => i.Message.Contains("base64", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GivenParametersWithUnresolvedRelativeReference_WhenValidating_ThenReferenceErrorFires()
    {
        // Arrange - Patient/1 exists as a parameter resource; Patient/2 does not.
        var result = ValidateFull("""
        {
            "resourceType": "Parameters",
            "parameter": [
                { "name": "patient", "resource": { "resourceType": "Patient", "id": "1" } },
                { "name": "coverage", "resource": {
                    "resourceType": "Coverage", "id": "c1", "status": "active",
                    "beneficiary": { "reference": "Patient/2" },
                    "payor": [ { "display": "x" } ] } }
            ]
        }
        """);

        // Act / Assert
        result.Issues.ShouldContain(i => i.Code == "ref-resolve" && i.Message.Contains("Patient/2", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenParametersWithResolvedRelativeReference_WhenValidating_ThenNoReferenceError()
    {
        // Arrange - the relative reference targets a sibling parameter resource that exists.
        var result = ValidateFull("""
        {
            "resourceType": "Parameters",
            "parameter": [
                { "name": "patient", "resource": { "resourceType": "Patient", "id": "1" } },
                { "name": "coverage", "resource": {
                    "resourceType": "Coverage", "id": "c1", "status": "active",
                    "beneficiary": { "reference": "Patient/1" },
                    "payor": [ { "display": "x" } ] } }
            ]
        }
        """);

        // Act / Assert
        result.Issues.ShouldNotContain(i => i.Code == "ref-resolve");
    }
}
