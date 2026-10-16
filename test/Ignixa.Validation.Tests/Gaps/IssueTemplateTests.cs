// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// The additive API of Task 9b: issue templates (item 6) and the host reference-type resolver (item 7). These do not compile
/// at 635c8f6, where neither exists.
/// </summary>
public class IssueTemplateTests
{
    // Resources raising issues from most of the converted checks, at Full depth.
    private static readonly string[] Corpus =
    [
        """{"resourceType":"Condition","id":"c","clinicalStatus":{"text":"active"},"subject":{"reference":"Organization/o"},"recordedDate":"2023-13-01","bogus":1}""",
        """{"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"valueQuantity":{"value":1,"comparator":"bogus"},"valueString":"x","subject":{"reference":"Patient/p","type":"Group"}}""",
        """{"resourceType":"Observation","id":"o","code":{"text":"x"},"effectiveDateTime":"2023-01-01T12:00:00","performer":[{"identifier":{"value":"1"}},{"display":"x"}]}""",
        """{"resourceType":"Patient","id":"p","maritalStatus":{"coding":[{"display":"x"},{"system":"local","code":"M"}]},"extension":[{"url":"http://example.org/ext","valueBoolean":true,"extension":[{"url":"a","valueString":"x"}]}],"_birthDate":{"extension":[{"url":"http://example.org/ext","valueBoolean":"no"}]},"birthDate":"2000-01-01"}""",
        """{"resourceType":"Patient","id":"p","active":"yes","gender":1,"contained":[{"resourceType":"Organization","id":"o"}],"managingOrganization":{"reference":"#missing"}}""",
    ];

    // Guard: every templated issue's message is its template rendered with its arguments, and names a published template.
    [Fact]
    public void GivenIssuesFromTheValidatorsChecks_WhenTemplated_ThenEachMessageIsItsTemplateRenderedWithItsArguments()
    {
        var templates = IssueTemplates.All.ToDictionary(template => template.Id, StringComparer.Ordinal);
        List<ValidationIssue> templated = [];
        foreach (var resource in Corpus)
        {
            templated.AddRange(JsonResourceValidator.Validate(resource, ValidationDepth.Full).Issues.Where(issue => issue.TemplateId is not null));
        }

        templated.Count.ShouldBeGreaterThan(15);
        foreach (var issue in templated)
        {
            templates.ShouldContainKey(issue.TemplateId!);
            templates[issue.TemplateId!].Render(issue.TemplateArguments).ShouldBe(issue.Message, issue.TemplateId);
        }

        templated.Select(issue => issue.TemplateId).Distinct().Count().ShouldBeGreaterThan(10);
    }

    // Guard: template ids are unique, so a table keyed on them is well defined.
    [Fact]
    public void GivenThePublishedTemplates_WhenListed_ThenTheirIdsAreUnique()
    {
        IssueTemplates.All.Select(template => template.Id).ShouldBeUnique();
    }

    // Guard: a converted check keeps its message byte for byte, and states the data it interpolates as arguments.
    [Fact]
    public void GivenACardinalityViolation_WhenReported_ThenItsMessageIsUnchangedAndItsArgumentsAreItsParts()
    {
        var issue = ValidationIssue.CardinalityViolation("Patient.contact[0].name", min: 1, max: null, actual: 0);

        issue.Message.ShouldBe("Patient.contact[0].name must have at least 1 occurrence(s), but found 0");
        issue.TemplateId.ShouldBe(IssueTemplates.CardinalityBelowMinimum.Id);
        issue.TemplateArguments.ShouldBe(["Patient.contact[0].name", "1", "0"]);
    }

    // Guard: the text-only binding error names its ValueSet as its only argument, so a consumer need not parse the message.
    [Fact]
    public void GivenATextOnlyConceptUnderARequiredBinding_WhenReported_ThenItsArgumentIsTheValueSet()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Condition","id":"c","clinicalStatus":{"text":"active"},"subject":{"reference":"Patient/p"},"code":{"text":"x"}}
            """, ValidationDepth.Spec);

        var issue = result.Issues.Where(i => i.TemplateId == IssueTemplates.BindingRequiredTextOnly.Id).ShouldHaveSingleItem();
        issue.TemplateArguments.ShouldBe(["http://hl7.org/fhir/ValueSet/condition-clinical|4.0.1"]);
    }

    // Guard: an issue built from free text has no template, and says so, rather than a fabricated one.
    [Fact]
    public void GivenAnIssueBuiltFromFreeText_WhenRead_ThenItHasNoTemplate()
    {
        var issue = new ValidationIssue(IssueSeverity.Error, "custom", "Patient", "anything");

        issue.TemplateId.ShouldBeNull();
        issue.TemplateArguments.ShouldBeEmpty();
    }

    // Guard: a template given the wrong number of arguments is a programmer error, raised at once.
    [Fact]
    public void GivenTheWrongNumberOfArguments_WhenAnIssueIsBuiltFromATemplate_ThenItThrows()
    {
        Should.Throw<ArgumentException>(() => ValidationIssue.FromTemplate(IssueSeverity.Error, "x", "Patient", IssueTemplates.ReferenceTargetType, "Patient"));
    }

    // Regression (new API): the host resolver tells the type of a urn the resource cannot, so its target type is checked.
    [Theory]
    [InlineData("Organization", true)]
    [InlineData("Patient", false)]
    public void GivenAHostResolverForAUrn_WhenValidated_ThenTheResolvedTypeIsChecked(string resolvedType, bool rejected)
    {
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec, ReferenceTypeResolver = new FixedTypes(new() { ["urn:uuid:1"] = resolvedType }) };
        var resolver = new ProfileAwareValidationSchemaResolver(new StructureDefinitionSchemaResolver(TestSchemaProvider.GetR4Schema()));

        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:1"}}
            """, settings, resolver);

        result.Issues.Any(i => i.Code == "reference-target-type").ShouldBe(rejected);
        result.Issues.ShouldNotContain(i => i.Code == "reference-target-unchecked");
    }

    private sealed class FixedTypes(Dictionary<string, string> types) : IReferenceTypeResolver
    {
        public string? ResolveResourceType(string reference) => types.GetValueOrDefault(reference);
    }
}
