// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// Reference target types (<c>targetProfile</c>, Task 9b item 7). Regressions fail at 635c8f6, where no reference's target
/// type was ever checked.
/// </summary>
public class ReferenceTargetTypeTests
{
    // Regression: at 635c8f6 each passed with no reference issue.
    [Theory]
    [InlineData("""{"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"subject":{"reference":"Organization/o1"}}""", "Observation.subject")]
    [InlineData("""{"resourceType":"Patient","id":"p","managingOrganization":{"reference":"https://example.org/fhir/Patient/p2/_history/3"}}""", "Patient.managingOrganization")]
    [InlineData("""{"resourceType":"Patient","id":"p","generalPractitioner":[{"reference":"https://other.example/fhir/Group/abc_def"}]}""", "Patient.generalPractitioner[0]")]
    [InlineData("""{"resourceType":"MedicationRequest","id":"m","status":"active","intent":"order","medicationCodeableConcept":{"text":"x"},"subject":{"reference":"Patient/p"},"reportedReference":{"reference":"Medication/x"}}""", "MedicationRequest.reportedReference")]
    [InlineData("""{"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"contained":[{"resourceType":"Organization","id":"c1","name":"x"}],"subject":{"reference":"#c1"},"performer":[{"reference":"#c1"}]}""", "Observation.subject")]
    public void GivenAReferenceToATypeTheElementDoesNotAllow_WhenValidatedAtSpec_ThenItIsAnError(string resource, string path)
    {
        var result = JsonResourceValidator.Validate(resource, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.Where(i => i.Code == "reference-target-type").ShouldHaveSingleItem().Path.ShouldBe(path);
    }

    // Regression: at 635c8f6 a Reference.type disagreeing with the reference passed.
    [Fact]
    public void GivenAReferenceWhoseTypeDisagreesWithIt_WhenValidatedAtSpec_ThenItIsAMismatch()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/p1","type":"Group"}}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Code == "reference-type-mismatch" && i.Path == "Observation.subject");
        result.Issues.ShouldNotContain(i => i.Code == "reference-target-type");
    }

    // Regression: at 635c8f6 a reference whose target type cannot be told passed silently; now it is reported as unchecked.
    [Fact]
    public void GivenReferencesWhoseTargetCannotBeTold_WhenValidatedAtSpec_ThenEachIsReportedUncheckedWithoutFailing()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","generalPractitioner":[{"reference":"urn:uuid:9b1f6c1e-0000-4000-8000-000000000001"},{"identifier":{"system":"http://example.org/ids","value":"1"}},{"display":"Dr X"},{"reference":"https://portal.example.org/Practitioner/profile.html"}]}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeTrue();
        result.Issues.Where(i => i.Code == "reference-target-unchecked").Select(i => (i.Severity, i.Path)).ShouldBe(
        [
            (IssueSeverity.Information, "Patient.generalPractitioner[0]"),
            (IssueSeverity.Information, "Patient.generalPractitioner[1]"),
            (IssueSeverity.Information, "Patient.generalPractitioner[2]"),
            (IssueSeverity.Information, "Patient.generalPractitioner[3]"),
        ]);
    }

    // Regression: at 635c8f6 a profile's narrower targetProfile was not applied.
    [Fact]
    public void GivenAProfileNarrowingATarget_WhenAResourceReferencesATypeOnlyTheCoreAllows_ThenItIsAnError()
    {
        var resolver = JsonResourceValidator.WithDefinitions("""
            {"resourceType":"StructureDefinition","id":"gp-org","url":"http://example.org/fhir/StructureDefinition/gp-org","version":"1.0.0","name":"GpOrg","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Patient","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Patient","derivation":"constraint",
             "differential":{"element":[{"id":"Patient.generalPractitioner","path":"Patient.generalPractitioner","type":[{"code":"Reference","targetProfile":["http://hl7.org/fhir/StructureDefinition/Organization"]}]}]}}
            """);

        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","meta":{"profile":["http://example.org/fhir/StructureDefinition/gp-org"]},"generalPractitioner":[{"reference":"Practitioner/x"}]}
            """, new ValidationSettings { Depth = ValidationDepth.Spec }, resolver);

        result.Issues.ShouldContain(i => i.Code == "reference-target-type" && i.Path == "Patient.generalPractitioner[0]");
    }

    // Guard: a profile that does not constrain a Reference keeps every target its core base allows (the projected core
    // snapshot carries all of them, not the first only), so its schema does not reject a target the core allows.
    [Fact]
    public void GivenADifferentialOnlyProfileNotConstrainingAReference_WhenItReferencesASecondCoreTarget_ThenItIsAccepted()
    {
        var resolver = JsonResourceValidator.WithDefinitions("""
            {"resourceType":"StructureDefinition","id":"obs-min","url":"http://example.org/fhir/StructureDefinition/obs-min","version":"1.0.0","name":"ObsMin","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Observation","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Observation","derivation":"constraint",
             "differential":{"element":[{"id":"Observation.code","path":"Observation.code","short":"x"}]}}
            """);

        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","meta":{"profile":["http://example.org/fhir/StructureDefinition/obs-min"]},"status":"final","code":{"text":"x"},"subject":{"reference":"Group/g1"}}
            """, new ValidationSettings { Depth = ValidationDepth.Spec }, resolver);

        result.Issues.ShouldNotContain(i => i.Code == "reference-target-type");
    }

    // Guard: references to allowed types raise nothing.
    [Fact]
    public void GivenReferencesToAllowedTypes_WhenValidatedAtSpec_ThenNoReferenceIssueIsRaised()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","managingOrganization":{"reference":"Organization/o1","type":"Organization"},"generalPractitioner":[{"reference":"Practitioner/x/_history/2"},{"reference":"https://example.org/fhir/Organization/o1"}],"link":[{"other":{"reference":"RelatedPerson/r"},"type":"seealso"}]}
            """, ValidationDepth.Spec);

        result.Issues.ShouldNotContain(i => i.Code.StartsWith("reference-", StringComparison.Ordinal));
    }

    // Guard: Compatibility depth, which follows the Microsoft FHIR Server, does not check target types.
    [Fact]
    public void GivenAReferenceToATypeTheElementDoesNotAllow_WhenValidatedAtCompatibility_ThenItIsNotChecked()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"subject":{"reference":"Organization/o1"}}
            """, ValidationDepth.Compatibility);

        result.Issues.ShouldNotContain(i => i.Code.StartsWith("reference-", StringComparison.Ordinal));
    }
}
