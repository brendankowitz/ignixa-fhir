// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// Extensions validated as the Extension datatype and against their loaded definitions (Task 9b item 2). Regressions fail at
/// 635c8f6, where no extension was validated at any depth: the schema builder never built a nested schema for one.
/// </summary>
public class ExtensionValidationTests
{
    private const string CodeExtensionDefinition = """
        {"resourceType":"StructureDefinition","id":"ext-code","url":"http://example.org/fhir/StructureDefinition/ext-code","version":"1.0.0","name":"ExtCode","status":"active","fhirVersion":"4.0.1",
         "kind":"complex-type","abstract":false,"context":[{"type":"element","expression":"Patient"}],"type":"Extension","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Extension","derivation":"constraint",
         "differential":{"element":[{"id":"Extension","path":"Extension","max":"1"},{"id":"Extension.extension","path":"Extension.extension","max":"0"},
           {"id":"Extension.url","path":"Extension.url","fixedUri":"http://example.org/fhir/StructureDefinition/ext-code"},
           {"id":"Extension.value[x]","path":"Extension.value[x]","min":1,"type":[{"code":"code"}],"binding":{"strength":"required","valueSet":"http://hl7.org/fhir/ValueSet/administrative-gender"}}]}}
        """;

    private const string Narrative = """ "text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>"} """;

    // Regression: at 635c8f6 an extension's value was never validated, so a string where Quantity.value needs a number passed.
    [Theory]
    [InlineData(ValidationDepth.Spec)]
    [InlineData(ValidationDepth.Full)]
    public void GivenAnExtensionValueBreakingItsDatatype_WhenValidated_ThenTheValueIsRejected(ValidationDepth depth)
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","extension":[{"url":"http://example.org/ext","valueQuantity":{"value":"1"}}]}
            """, depth);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Code == "type-1" && i.Path == "Patient.extension[0].valueQuantity.value");
    }

    // Regression: at 635c8f6 a primitive's extensions (the _birthDate shadow) were not validated either.
    [Fact]
    public void GivenAPrimitiveExtensionWithAnInvalidValue_WhenValidatedAtSpec_ThenTheValueIsRejected()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","birthDate":"2000-01-01","_birthDate":{"extension":[{"url":"http://example.org/ext","valueBoolean":"no"}]}}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Code == "type-1" && i.Path.StartsWith("Patient.birthDate.extension[0]", StringComparison.Ordinal));
    }

    // Regression: at 635c8f6 an extension inside a CodeableConcept (which has no nested schema) went unvalidated.
    [Fact]
    public void GivenAnExtensionWithoutAUrlInsideACodeableConcept_WhenValidatedAtSpec_ThenItIsRejected()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","maritalStatus":{"text":"x","extension":[{"valueString":"no url"}]}}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "cardinality-violation" && i.Path.StartsWith("Patient.maritalStatus.extension[0]", StringComparison.Ordinal));
    }

    // Regression: at 635c8f6 ext-1 was never evaluated on an extension (its scope is Extension, which no schema validated).
    [Fact]
    public void GivenAnExtensionWithBothAValueAndNestedExtensions_WhenValidatedAtFull_ThenExt1Fails()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","extension":[{"url":"http://example.org/ext","valueBoolean":true,"extension":[{"url":"a","valueString":"x"}]}]}
            """, ValidationDepth.Full);

        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Code == "ext-1" && i.Path == "Patient.extension[0]");
    }

    // Regression: at 635c8f6 an extension with no loaded definition passed silently; now it is reported as unchecked.
    [Fact]
    public void GivenAnExtensionWithoutALoadedDefinition_WhenValidatedAtFull_ThenItIsReportedAsCheckedOnlyAsADatatype()
    {
        var result = JsonResourceValidator.Validate($$"""
            {"resourceType":"Patient","id":"p","extension":[{"url":"http://example.org/ext","valueString":"ok"}],{{Narrative}}}
            """, ValidationDepth.Full);

        result.IsValid.ShouldBeTrue();
        var issue = result.Issues.Where(i => i.Code == "extension-definition-unchecked").ShouldHaveSingleItem();
        issue.Severity.ShouldBe(IssueSeverity.Information);
        issue.Path.ShouldBe("Patient.extension[0]");
    }

    // Regression: at 635c8f6 a loaded extension definition was never applied, so a value of a type it does not allow passed.
    [Fact]
    public void GivenALoadedExtensionDefinition_WhenAnExtensionUsesATypeItDoesNotAllow_ThenItIsRejectedAtFull()
    {
        var resolver = JsonResourceValidator.WithDefinitions(CodeExtensionDefinition);

        var result = JsonResourceValidator.Validate($$"""
            {"resourceType":"Patient","id":"p","extension":[{"url":"http://example.org/fhir/StructureDefinition/ext-code","valueString":"male"}],{{Narrative}}}
            """, new ValidationSettings { Depth = ValidationDepth.Full }, resolver);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "choice-invalid-type" && i.Path == "Patient.extension[0].value");
        result.Issues.ShouldNotContain(i => i.Code == "extension-definition-unchecked");
    }

    // Regression: at 635c8f6 a loaded definition's binding on value[x] was not applied (the [x] path never navigated).
    [Fact]
    public void GivenALoadedExtensionDefinitionWithARequiredBinding_WhenTheCodeIsNotInTheValueSet_ThenItIsRejectedAtFull()
    {
        var resolver = JsonResourceValidator.WithDefinitions(CodeExtensionDefinition);

        var result = JsonResourceValidator.Validate($$"""
            {"resourceType":"Patient","id":"p","extension":[{"url":"http://example.org/fhir/StructureDefinition/ext-code","valueCode":"martian"}],{{Narrative}}}
            """, new ValidationSettings { Depth = ValidationDepth.Full }, resolver);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Code == "code-invalid" && i.Path == "Patient.extension[0].valueCode");
    }

    // Guard: an extension conforming to its loaded definition raises nothing, and is not reported as unchecked.
    [Fact]
    public void GivenALoadedExtensionDefinition_WhenAnExtensionConforms_ThenTheResourceIsValidWithoutAnUncheckedNote()
    {
        var resolver = JsonResourceValidator.WithDefinitions(CodeExtensionDefinition);

        var result = JsonResourceValidator.Validate($$"""
            {"resourceType":"Patient","id":"p","extension":[{"url":"http://example.org/fhir/StructureDefinition/ext-code","valueCode":"male"}],{{Narrative}}}
            """, new ValidationSettings { Depth = ValidationDepth.Full }, resolver);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    // Guard: Minimal depth validates no extension, as it validates no nested datatype (a url-less extension would fail even
    // the Extension datatype's universal cardinality check).
    [Fact]
    public void GivenAnInvalidExtension_WhenValidatedAtMinimal_ThenItIsNotChecked()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","extension":[{"valueQuantity":{"value":"1"}}]}
            """, ValidationDepth.Minimal);

        result.Issues.ShouldNotContain(i => i.Path.StartsWith("Patient.extension", StringComparison.Ordinal));
    }

    // Regression: at 635c8f6 a contained resource's extension was not validated; it is reported once, by the contained resource's
    // own schema (the container's walk does not enter contained resources).
    [Fact]
    public void GivenAContainedResourceWithAnInvalidExtension_WhenValidatedAtSpec_ThenItIsReportedOnce()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","contained":[{"resourceType":"Organization","id":"o","extension":[{"url":"http://example.org/ext","valueQuantity":{"value":"1"}}]}],"managingOrganization":{"reference":"#o"}}
            """, ValidationDepth.Spec);

        result.Issues.Count(i => i.Code == "type-1" && i.Path.EndsWith("valueQuantity.value", StringComparison.Ordinal)).ShouldBe(1);
    }
}
