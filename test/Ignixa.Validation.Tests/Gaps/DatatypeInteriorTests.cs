// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// The insides of CodeableConcept, Coding and Reference are validated structurally like any other datatype (Task 9b fix
/// round 1, F4). Regressions fail at b0f34da, where those three were never descended into.
/// </summary>
public class DatatypeInteriorTests
{
    // Regression: at b0f34da unknown properties and wrong JSON types inside these datatypes passed.
    [Theory]
    [InlineData(ValidationDepth.Spec)]
    [InlineData(ValidationDepth.Full)]
    public void GivenBrokenCodeableConceptCodingAndReferenceInteriors_WhenValidated_ThenEachIsRejected(ValidationDepth depth)
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o1","status":"final","text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>"},
             "code":{"coding":[{"system":"http://loinc.org","code":"1-8","bogus":1,"version":5}],"foo":"x"},
             "subject":{"reference":"Patient/1","actor":{"display":"y"}},
             "performer":[{"reference":123}]}
            """, depth);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "unknown-property" && i.Path == "Observation.code.foo");
        result.Issues.ShouldContain(i => i.Code == "unknown-property" && i.Path == "Observation.code.coding[0].bogus");
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path == "Observation.code.coding[0].version");
        result.Issues.ShouldContain(i => i.Code == "unknown-property" && i.Path == "Observation.subject.actor");
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path == "Observation.performer[0].reference");
    }

    // Regression: at b0f34da an element repeated beyond its cardinality inside a Coding passed.
    [Fact]
    public void GivenACodingWithARepeatedCode_WhenValidatedAtSpec_ThenTheCardinalityIsRejected()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"coding":[{"system":"http://loinc.org","code":["1-8","2-6"]}]}}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path.StartsWith("Observation.code.coding[0].code", StringComparison.Ordinal));
    }

    // Guard: a Reference's own invariant ref-1 now runs, and (as R5 corrected it) passes a Reference with no 'reference'.
    [Fact]
    public void GivenReferencesWithOnlyAnIdentifierOrADisplay_WhenValidatedAtFull_ThenRef1DoesNotFail()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>"},
             "subject":{"identifier":{"value":"1"}},"performer":[{"display":"John"}]}
            """, ValidationDepth.Full);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotContain(i => i.Code == "ref-1");
    }

    // Regression: at b0f34da a Reference's own invariant ref-1 never ran on a reference naming no contained resource.
    [Fact]
    public void GivenALocalReferenceNamingNothingContained_WhenValidatedAtFull_ThenRef1Fails()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>"},"subject":{"reference":"#zz"}}
            """, ValidationDepth.Full);

        result.Issues.ShouldContain(i => i.Code == "ref-1" && i.Path == "Observation.subject");
    }
}
