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
    private static readonly string ItemExtension = Definition("ext-item", """[{"type":"element","expression":"Questionnaire.item"}]""");
    private static readonly string CanonicalResourceExtension = Definition("ext-canonical", """[{"type":"element","expression":"CanonicalResource"}]""");
    private static readonly string ExcludeExtension = Definition("ext-exclude", """[{"type":"element","expression":"ValueSet.compose.exclude"}]""");
    private static readonly string StringExtension = Definition("ext-string", """[{"type":"element","expression":"string"}]""");
    private static readonly string ElementIdExtension = Definition("ext-elementid", """[{"type":"element","expression":"http://example.org/fhir/StructureDefinition/p#Patient.name:official"}]""");
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

    // Regression (PR #516 review, finding 1): a nested item's path was built from instance names
    // (Questionnaire.item.item), so a context naming Questionnaire.item, as SDC's questionnaire-itemControl does, failed on it.
    [Fact]
    public void GivenAQuestionnaireItemContextExtensionOnANestedItem_WhenValidatedAtFull_ThenNoContextIssueIsRaised()
    {
        var result = Validate("""
            {"resourceType":"Questionnaire","id":"q","name":"Q","status":"active","item":[{"linkId":"1","type":"group","text":"g","item":[{"linkId":"1.1","type":"string","text":"s","extension":[{"url":"@ext-item","valueCode":"male"}]}]}],@TEXT}
            """, ItemExtension);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    // Guard (PR #516 review round 2, finding 1): a non-recursive element that reuses another's definition
    // (ValueSet.compose.exclude reuses #ValueSet.compose.include) is still named by its own path.
    [Fact]
    public void GivenAValueSetExcludeContextExtensionOnAnExclude_WhenValidatedAtFull_ThenNoContextIssueIsRaised()
    {
        var result = Validate("""
            {"resourceType":"ValueSet","id":"v","name":"V","status":"active","compose":{"include":[{"system":"http://example.org/a"}],"exclude":[{"system":"http://example.org/b","extension":[{"url":"@ext-exclude","valueCode":"male"}]}]},@TEXT}
            """, ExcludeExtension);

        result.Issues.ShouldNotContain(i => i.Code == "extension-context" || i.Code == "extension-context-unchecked");
    }

    // Regression (PR #516 review round 2, finding 4): a context naming the primitive a primitive specializes (string for
    // code) failed on it; only Quantity's specializations were matched.
    [Fact]
    public void GivenAStringContextExtensionOnACodeElement_WhenValidatedAtFull_ThenNoContextIssueIsRaised()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","gender":"male","_gender":{"extension":[{"url":"@ext-string","valueCode":"male"}]},@TEXT}
            """, StringExtension);

        result.Issues.ShouldNotContain(i => i.Code == "extension-context" || i.Code == "extension-context-unchecked");
    }

    // Guard (PR #516 review round 2, finding 3): CanonicalResource can allow only a resource root, so on a datatype it is
    // decided, and a mismatch stays an error rather than being reported unchecked.
    [Fact]
    public void GivenAnExtensionWithACanonicalResourceContextOnAnObservationCode_WhenValidatedAtFull_ThenItIsAnError()
    {
        var result = Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x","extension":[{"url":"@ext-canonical","valueCode":"male"}]},@TEXT}
            """, CanonicalResourceExtension);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Code == "extension-context" && i.Path == "Observation.code.extension[0]");
    }

    // Regression (PR #516 review, finding 2): a context naming an abstract type the schema carries no members for
    // (CanonicalResource) failed on every resource; the docs promise specialization matching, so can't-tell is reported.
    [Fact]
    public void GivenAnExtensionWithACanonicalResourceContextOnAValueSet_WhenValidatedAtFull_ThenItsContextIsReportedUncheckedWithoutFailing()
    {
        var result = Validate("""
            {"resourceType":"ValueSet","id":"v","name":"V","status":"active","extension":[{"url":"@ext-canonical","valueCode":"male"}],@TEXT}
            """, CanonicalResourceExtension);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Information && i.Code == "extension-context-unchecked" && i.Path == "ValueSet.extension[0]");
    }

    // Regression (PR #516 review, finding 3): R5 lets an element context be a profile-qualified element id
    // (defining-extensions.html: "[url]#[elementid]"), which instance paths cannot spell, so it failed everywhere.
    [Fact]
    public void GivenAnExtensionWithAProfileQualifiedElementIdContext_WhenValidatedAtFull_ThenItsContextIsReportedUncheckedWithoutFailing()
    {
        var result = Validate("""
            {"resourceType":"Patient","id":"p","name":[{"family":"x","extension":[{"url":"@ext-elementid","valueCode":"male"}]}],@TEXT}
            """, ElementIdExtension);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Information && i.Code == "extension-context-unchecked" && i.Path == "Patient.name[0].extension[0]");
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
