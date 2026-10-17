// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// Where an extension may be used: its definition's contexts and modifier status (F2), and the relative url only a
/// sub-extension may carry (F6), from Task 9b fix round 1. Regressions fail at b0f34da, where neither was checked.
/// </summary>
public class ExtensionUseTests
{
    private const string Narrative = """ "text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>"} """;

    private static readonly string PatientExtension = Definition("ext-patient", """[{"type":"element","expression":"Patient"}]""");
    private static readonly string HumanNameExtension = Definition("ext-name", """[{"type":"element","expression":"HumanName"}]""");
    private static readonly string PathExtension = Definition("ext-path", """[{"type":"element","expression":"Patient.name"}]""");
    private static readonly string ModifierExtension = Definition("ext-mod", """[{"type":"element","expression":"Patient"}]""", isModifier: true);
    private static readonly string FhirPathExtension = Definition("ext-fp", """[{"type":"fhirpath","expression":"Patient.name.where(use = 'official')"}]""");
    private static readonly string SubExtension = Definition("ext-sub", """[{"type":"extension","expression":"http://example.org/fhir/StructureDefinition/parent"}]""");

    // Regression: at b0f34da an extension defined for Patient passed on an Observation.
    [Fact]
    public void GivenAnExtensionUsedOnAResourceItsContextsDoNotName_WhenValidatedAtFull_ThenItIsAnError()
    {
        var result = Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"extension":[{"url":"@ext-patient","valueCode":"male"}],@TEXT}
            """, PatientExtension);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Code == "extension-context" && i.Path == "Observation.extension[0]");
    }

    // Regression: at b0f34da an extension defined for the Patient resource passed inside one of its datatypes.
    [Fact]
    public void GivenAResourceContextExtensionUsedInsideADatatype_WhenValidatedAtFull_ThenItIsAnError()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","name":[{"family":"x","extension":[{"url":"@ext-patient","valueCode":"male"}]}],@TEXT}
            """, PatientExtension);

        result.Issues.ShouldContain(i => i.Code == "extension-context" && i.Path == "Patient.name[0].extension[0]");
    }

    // Guard: an extension used where its context names the datatype, the element path or the resource is accepted.
    [Theory]
    [InlineData("ext-name")]
    [InlineData("ext-path")]
    public void GivenAnExtensionUsedWhereItsContextAllows_WhenValidatedAtFull_ThenNoContextIssueIsRaised(string id)
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","name":[{"family":"x","extension":[{"url":"@ID","valueCode":"male"}]}],@TEXT}
            """.Replace("@ID", "@" + id, StringComparison.Ordinal), HumanNameExtension, PathExtension);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    // Regression: at b0f34da a modifier extension carried in 'extension' passed.
    [Fact]
    public void GivenAModifierExtensionUsedAsAnExtension_WhenValidatedAtFull_ThenItIsAnError()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","extension":[{"url":"@ext-mod","valueCode":"male"}],@TEXT}
            """, ModifierExtension);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "extension-modifier-mismatch" && i.Path == "Patient.extension[0]");
    }

    // Regression: at b0f34da a non-modifier extension carried in 'modifierExtension' passed.
    [Fact]
    public void GivenANonModifierExtensionUsedAsAModifierExtension_WhenValidatedAtFull_ThenItIsAnError()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","modifierExtension":[{"url":"@ext-patient","valueCode":"male"}],@TEXT}
            """, PatientExtension);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "extension-modifier-mismatch" && i.Path == "Patient.modifierExtension[0]");
    }

    // Guard: a modifier extension in 'modifierExtension' where its context allows it is accepted.
    [Fact]
    public void GivenAModifierExtensionUsedAsOne_WhenValidatedAtFull_ThenItIsAccepted()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","modifierExtension":[{"url":"@ext-mod","valueCode":"male"}],@TEXT}
            """, ModifierExtension);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    // Regression: at b0f34da a FHIRPath context was neither checked nor reported as unchecked.
    [Fact]
    public void GivenAnExtensionWithAFhirPathContext_WhenValidatedAtFull_ThenItsContextIsReportedUncheckedWithoutFailing()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","name":[{"family":"x","extension":[{"url":"@ext-fp","valueCode":"male"}]}],@TEXT}
            """, FhirPathExtension);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Information && i.Code == "extension-context-unchecked" && i.Path == "Patient.name[0].extension[0]");
    }

    // Regression: at b0f34da an extension whose context is another extension passed outside it.
    [Fact]
    public void GivenAnExtensionContextExtensionUsedOutsideItsParent_WhenValidatedAtFull_ThenItIsAnError()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","extension":[{"url":"@ext-sub","valueCode":"male"}],@TEXT}
            """, SubExtension);

        result.Issues.ShouldContain(i => i.Code == "extension-context" && i.Path == "Patient.extension[0]");
    }

    // Guard: inside the extension its context names, it is accepted.
    [Fact]
    public void GivenAnExtensionContextExtensionUsedInsideItsParent_WhenValidatedAtFull_ThenNoContextIssueIsRaised()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","extension":[{"url":"http://example.org/fhir/StructureDefinition/parent","extension":[{"url":"@ext-sub","valueCode":"male"}]}],@TEXT}
            """, SubExtension);

        result.Issues.ShouldNotContain(i => i.Code == "extension-context" || i.Code == "extension-context-unchecked");
    }

    // Regression: at b0f34da a resource-level extension with a relative url passed.
    [Theory]
    [InlineData(ValidationDepth.Spec)]
    [InlineData(ValidationDepth.Full)]
    public void GivenATopLevelExtensionWithARelativeUrl_WhenValidated_ThenItIsAnError(ValidationDepth depth)
    {
        var result = JsonResourceValidator.Validate(Expand("""
            {"resourceType":"Patient","id":"p","extension":[{"url":"foo","valueString":"x"}],@TEXT}
            """), depth);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Code == "extension-url-relative" && i.Path == "Patient.extension[0]");
    }

    // Regression: at 758e490 '/foo' passed as absolute on Unix (Uri reads it as a file path), so it was not reported.
    [Fact]
    public void GivenATopLevelExtensionWithARootRelativeUrl_WhenValidatedAtSpec_ThenItIsAnError()
    {
        var result = JsonResourceValidator.Validate(Expand("""
            {"resourceType":"Patient","id":"p","extension":[{"url":"/foo","valueString":"x"}],@TEXT}
            """), ValidationDepth.Spec);

        result.Issues.ShouldContain(i => i.Code == "extension-url-relative" && i.Path == "Patient.extension[0]");
    }

    // Regression: at 758e490 a Coding.system of '/foo' passed as absolute on Unix.
    [Fact]
    public void GivenACodingSystemWithARootRelativeUrl_WhenValidatedAtSpec_ThenItIsAnError()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"coding":[{"system":"/foo","code":"x"}]}}
            """, ValidationDepth.Spec);

        result.Issues.ShouldContain(i => i.Code == "coding-system-absolute" && i.Path == "Observation.code.coding[0].system");
    }

    // Guard: a complex extension's sub-extensions may use relative urls.
    [Fact]
    public void GivenASubExtensionWithARelativeUrl_WhenValidatedAtSpec_ThenItIsAccepted()
    {
        var result = JsonResourceValidator.Validate(Expand("""
            {"resourceType":"Patient","id":"p","extension":[{"url":"http://example.org/fhir/StructureDefinition/parent","extension":[{"url":"part","valueString":"x"}]}],@TEXT}
            """), ValidationDepth.Spec);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotContain(i => i.Code == "extension-url-relative");
    }

    private static ValidationResult Validate(string json, params string[] definitions) =>
        JsonResourceValidator.Validate(Expand(json), new ValidationSettings { Depth = ValidationDepth.Full }, JsonResourceValidator.WithDefinitions(definitions));

    private static string Url(string id) => $"http://example.org/fhir/StructureDefinition/{id}";

    private static string Definition(string id, string contexts, bool isModifier = false) => """
        {"resourceType":"StructureDefinition","id":"@ID","url":"@URL","version":"1.0.0","name":"Ext","status":"active","fhirVersion":"4.0.1",
         "kind":"complex-type","abstract":false,"context":@CONTEXTS,"type":"Extension","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Extension","derivation":"constraint",
         "differential":{"element":[{"id":"Extension","path":"Extension","max":"1"@MODIFIER},
           {"id":"Extension.extension","path":"Extension.extension","max":"0"},
           {"id":"Extension.url","path":"Extension.url","fixedUri":"@URL"},
           {"id":"Extension.value[x]","path":"Extension.value[x]","min":1,"type":[{"code":"code"}]}]}}
        """
        .Replace("@ID", id, StringComparison.Ordinal)
        .Replace("@URL", Url(id), StringComparison.Ordinal)
        .Replace("@CONTEXTS", contexts, StringComparison.Ordinal)
        .Replace("@MODIFIER", isModifier ? ",\"isModifier\":true,\"isModifierReason\":\"Changes the meaning\"" : string.Empty, StringComparison.Ordinal);

    // "@ext-x" stands for that extension's url and "@TEXT" for a narrative, keeping the JSON free of interpolation braces.
    private static string Expand(string json) =>
        Regex.Replace(json.Replace("@TEXT", Narrative, StringComparison.Ordinal), "@(ext-[a-z]+)", m => Url(m.Groups[1].Value));
}
