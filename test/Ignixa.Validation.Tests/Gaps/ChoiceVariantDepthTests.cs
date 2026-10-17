// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// One depth policy for an element whether or not it is a choice (Task 9b items 3 and 4): a complex choice variant is
/// validated wherever a non-choice element of its type is (Spec and above), with the checks that type gets, and a dateTime
/// follows one timezone rule on both paths. Regressions fail at 635c8f6.
/// </summary>
public class ChoiceVariantDepthTests
{
    // Regression: at 635c8f6 valueQuantity's subtree ran only at Full, so its required comparator binding passed at Spec.
    [Fact]
    public void GivenAComplexChoiceVariantBreakingItsDatatype_WhenValidatedAtSpec_ThenItIsRejected()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"valueQuantity":{"value":1,"comparator":"bogus"}}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path == "Observation.valueQuantity.comparator");
    }

    // Guard: a non-choice element of the same datatype was already validated at Spec; the choice path now matches it.
    [Fact]
    public void GivenANonChoiceElementBreakingTheSameDatatype_WhenValidatedAtSpec_ThenItIsRejected()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"referenceRange":[{"low":{"value":1,"comparator":"bogus"}}]}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path.StartsWith("Observation.referenceRange[0].low.comparator", StringComparison.Ordinal));
    }

    // Regression: at 635c8f6 a CodeableConcept choice variant got no coding structure check (its non-choice twin does).
    [Fact]
    public void GivenACodeableConceptChoiceVariantWithAnEmptyCoding_WhenValidatedAtSpec_ThenCoding1Warns()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"valueCodeableConcept":{"coding":[{"display":"positive"}]}}
            """, ValidationDepth.Spec);

        result.Issues.ShouldContain(i => i.Code == "coding-1" && i.Path == "Observation.valueCodeableConcept.coding[0]");
    }

    // Regression: at 635c8f6 the choice path rejected a dateTime the non-choice path accepted at Spec (bug #210-4 leniency).
    [Theory]
    [InlineData("2021-10-13+02:00")]
    [InlineData("2023-01-01T12:00:00")]
    public void GivenALooseChoiceDateTime_WhenValidatedAtSpec_ThenItIsAcceptedLikeANonChoiceOne(string dateTime)
    {
        var choice = JsonResourceValidator.Validate($$"""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"effectiveDateTime":"{{dateTime}}"}
            """, ValidationDepth.Spec);
        var nonChoice = JsonResourceValidator.Validate($$"""
            {"resourceType":"Condition","id":"c","subject":{"reference":"Patient/p"},"recordedDate":"{{dateTime}}"}
            """, ValidationDepth.Spec);

        choice.Issues.ShouldNotContain(i => i.Code == "type-1");
        nonChoice.Issues.ShouldNotContain(i => i.Code == "type-1");
    }

    // Guard: at Full both paths apply the strict R4 grammar.
    [Theory]
    [InlineData("2021-10-13+02:00")]
    [InlineData("2023-01-01T12:00:00")]
    public void GivenALooseDateTime_WhenValidatedAtFull_ThenBothPathsRejectIt(string dateTime)
    {
        var choice = JsonResourceValidator.Validate($$"""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"effectiveDateTime":"{{dateTime}}"}
            """, ValidationDepth.Full);
        var nonChoice = JsonResourceValidator.Validate($$"""
            {"resourceType":"Condition","id":"c","subject":{"reference":"Patient/p"},"recordedDate":"{{dateTime}}"}
            """, ValidationDepth.Full);

        choice.Issues.ShouldContain(i => i.Code == "type-1");
        nonChoice.Issues.ShouldContain(i => i.Code == "type-1");
    }

    // Regression: at 635c8f6 the non-choice Spec path checked no component range, so hour 25 passed; the leniency covers the
    // timezone only.
    [Fact]
    public void GivenADateTimeWithAnOutOfRangeHour_WhenValidatedAtSpec_ThenBothPathsRejectIt()
    {
        var choice = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"effectiveDateTime":"2023-01-01T25:00:00Z"}
            """, ValidationDepth.Spec);
        var nonChoice = JsonResourceValidator.Validate("""
            {"resourceType":"Condition","id":"c","subject":{"reference":"Patient/p"},"recordedDate":"2023-01-01T25:00:00Z"}
            """, ValidationDepth.Spec);

        choice.Issues.ShouldContain(i => i.Code == "type-1");
        nonChoice.Issues.ShouldContain(i => i.Code == "type-1");
    }
}
