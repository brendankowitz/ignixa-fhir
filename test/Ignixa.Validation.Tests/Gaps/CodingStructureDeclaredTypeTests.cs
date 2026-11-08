// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// <c>coding-1</c> decides Coding versus CodeableConcept by the element's declared type (Task 9b item 5). Regressions fail at
/// 635c8f6, where a text-only CodeableConcept was taken for a Coding without system or code, and the issue path lacked the
/// resource prefix.
/// </summary>
public class CodingStructureDeclaredTypeTests
{
    // Regression: at 635c8f6 each text-only reasonCode raised coding-1 (782 times on the NDJSON sample).
    [Fact]
    public void GivenTextOnlyCodeableConcepts_WhenValidatedAtSpec_ThenNoCodingWarningIsRaised()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Encounter","id":"e","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"reasonCode":[{"text":"pain"},{"text":"fever"}]}
            """, ValidationDepth.Spec);

        result.Issues.ShouldNotContain(i => i.Code == "coding-1");
    }

    // Regression: at 635c8f6 the path was "maritalStatus.coding", with neither the resource type nor the index.
    [Fact]
    public void GivenACodingWithNeitherSystemNorCode_WhenValidatedAtSpec_ThenTheWarningPathIsTheCodingsOwn()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","maritalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/v3-MaritalStatus","code":"M"},{"display":"Married"}]}}
            """, ValidationDepth.Spec);

        var issue = result.Issues.Where(i => i.Code == "coding-1").ShouldHaveSingleItem();
        issue.Severity.ShouldBe(IssueSeverity.Warning);
        issue.Path.ShouldBe("Patient.maritalStatus.coding[1]");
    }

    // Regression: at 635c8f6 a Coding-typed element without system or code was warned about at "class", without the resource
    // type; it is still warned about, now at its own path.
    [Fact]
    public void GivenACodingTypedElementWithNeitherSystemNorCode_WhenValidatedAtSpec_ThenItIsWarned()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Encounter","id":"e","status":"finished","class":{"display":"ambulatory"}}
            """, ValidationDepth.Spec);

        result.Issues.ShouldContain(i => i.Code == "coding-1" && i.Path == "Encounter.class");
    }
}
