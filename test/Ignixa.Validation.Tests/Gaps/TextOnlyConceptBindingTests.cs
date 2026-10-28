// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// A CodeableConcept with text only under a required binding (Task 9b item 1). Regressions fail at 635c8f6, where
/// BindingCheck returned success for a concept without codings; guards pin behaviour that must not move with the fix.
/// </summary>
public class TextOnlyConceptBindingTests
{
    private const string TextOnlyClinicalStatus = """
        {"resourceType":"Condition","id":"c","clinicalStatus":{"text":"active"},"subject":{"reference":"Patient/p"},"code":{"text":"x"}}
        """;

    // Regression: at 635c8f6 the text-only clinicalStatus (required binding to condition-clinical) passed with no issue.
    [Theory]
    [InlineData(ValidationDepth.Spec)]
    [InlineData(ValidationDepth.Full)]
    public void GivenATextOnlyConceptUnderARequiredBinding_WhenValidated_ThenItIsAnErrorNamingTheValueSet(ValidationDepth depth)
    {
        var result = JsonResourceValidator.Validate(TextOnlyClinicalStatus, depth);

        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.Where(i => i.Severity == IssueSeverity.Error).ShouldHaveSingleItem();
        issue.Code.ShouldBe("code-invalid");
        issue.Path.ShouldBe("Condition.clinicalStatus");
        issue.Message.ShouldContain("http://hl7.org/fhir/ValueSet/condition-clinical");
    }

    // Regression (PR #516 review, finding 5): the rule needs no terminology (R4 terminologies.html#strength), yet a schema
    // built without a terminology service had no BindingCheck, and SkipTerminologyValidation returned before the rule ran.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenATextOnlyConceptUnderARequiredBindingWithoutTerminology_WhenValidatedAtSpec_ThenItIsAnError(bool skipTerminology)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(TextOnlyClinicalStatus)!).ToElement(TestSchemaProvider.GetR4Schema());
        var schema = skipTerminology
            ? new StructureDefinitionSchemaResolver(TestSchemaProvider.GetR4Schema()).GetSchema("Condition")!
            : new StructureDefinitionSchemaBuilder().BuildSchema(TestSchemaProvider.GetR4Schema().GetTypeDefinition("Condition")!, TestSchemaProvider.GetR4Schema(), terminologyService: null);

        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec, SkipTerminologyValidation = skipTerminology });

        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.TemplateId == IssueTemplates.BindingRequiredTextOnly.Id && i.Path == "Condition.clinicalStatus");
    }

    // Regression: at 635c8f6 a text-only concept passed beside a coded one; only the text-only one is reported, at its path.
    [Fact]
    public void GivenTextOnlyConceptsInARepeatedRequiredElement_WhenValidatedAtSpec_ThenEachIsReportedAtItsOwnPath()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"AllergyIntolerance","id":"a","clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical","code":"active"}]},"verificationStatus":{"text":"confirmed"},"patient":{"reference":"Patient/p"}}
            """, ValidationDepth.Spec);

        result.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Path).ShouldBe(["AllergyIntolerance.verificationStatus"]);
    }

    // Guard: Compatibility depth keeps accepting text-only concepts, as the Microsoft FHIR Server does.
    [Fact]
    public void GivenATextOnlyConceptUnderARequiredBinding_WhenValidatedAtCompatibility_ThenItIsAccepted()
    {
        var result = JsonResourceValidator.Validate(TextOnlyClinicalStatus, ValidationDepth.Compatibility);

        result.Issues.ShouldNotContain(i => i.Severity == IssueSeverity.Error);
    }

    // Guard: a text-only concept under a binding that is not required asks for nothing it lacks.
    [Theory]
    [InlineData(ValidationDepth.Spec)]
    [InlineData(ValidationDepth.Full)]
    public void GivenATextOnlyConceptUnderAPreferredOrExampleBinding_WhenValidated_ThenNoBindingIssueIsRaised(ValidationDepth depth)
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Encounter","id":"e","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"reasonCode":[{"text":"pain"}],"type":[{"text":"visit"}]}
            """, depth);

        result.Issues.ShouldNotContain(i => i.Severity == IssueSeverity.Error);
        result.Issues.ShouldNotContain(i => i.Code == "code-invalid");
    }

    // Regression: at b0f34da a concept carrying only a data-absent-reason extension was "text-only" under a required binding.
    [Theory]
    [InlineData(ValidationDepth.Spec)]
    [InlineData(ValidationDepth.Full)]
    public void GivenAConceptWithOnlyADataAbsentReasonUnderARequiredBinding_WhenValidated_ThenTheBindingRaisesNothing(ValidationDepth depth)
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Condition","id":"c","clinicalStatus":{"extension":[{"url":"http://hl7.org/fhir/StructureDefinition/data-absent-reason","valueCode":"unknown"}]},"code":{"text":"x"},"subject":{"reference":"Patient/1"}}
            """, depth);

        result.Issues.ShouldNotContain(i => i.Severity == IssueSeverity.Error);
        result.Issues.ShouldNotContain(i => i.Path.StartsWith("Condition.clinicalStatus", StringComparison.Ordinal) && i.Code != "extension-definition-unchecked");
    }

    // Guard: a data-absent-reason does not excuse a coding outside the required ValueSet.
    [Fact]
    public void GivenAConceptWithADataAbsentReasonAndAWrongCoding_WhenValidatedAtSpec_ThenTheCodingIsRejected()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Condition","id":"c","clinicalStatus":{"extension":[{"url":"http://hl7.org/fhir/StructureDefinition/data-absent-reason","valueCode":"unknown"}],
             "coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"bogus"}]},"code":{"text":"x"},"subject":{"reference":"Patient/1"}}
            """, ValidationDepth.Spec);

        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path.StartsWith("Condition.clinicalStatus", StringComparison.Ordinal));
    }
}
