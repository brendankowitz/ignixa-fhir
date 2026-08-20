// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for <see cref="NestedValidationResultExtensions.IssuesOrSynthesizedFailure"/>, the shared
/// merge logic used by <see cref="NestedComplexTypeCheck"/>, <see cref="ChoiceVariantNestedCheck"/>,
/// and <see cref="ContainedResourceCheck"/> to fold a nested <see cref="ValidationResult"/> into their
/// own issue list.
/// </summary>
/// <remarks>
/// <see cref="Ignixa.Validation.Abstractions.ValidationSchema.Validate"/> combines its checks' results
/// via <see cref="ValidationResult.Combine"/>, which always re-derives <c>IsValid</c> from whether the
/// merged issues contain an Error/Fatal severity - so an invalid-with-zero-issues
/// <see cref="ValidationResult"/> can never actually come out of a real
/// <see cref="Ignixa.Validation.Abstractions.ValidationSchema"/>, and the three checks above can never
/// observe it through their public <c>Validate</c> method. The combination is nonetheless
/// representable through <see cref="ValidationResult"/>'s public constructor (nothing ties
/// <c>IsValid</c> to <c>Issues</c> there), which is exactly the gap this extension guards: an
/// <see cref="Ignixa.Validation.Abstractions.IValidationCheck"/> is a public extension point, and
/// nothing in its contract stops an implementation from returning that combination directly. These
/// tests exercise the guard at that level, since it is not reachable by driving the three checks
/// end-to-end.
/// </remarks>
public class NestedValidationResultExtensionsTests
{
    [Fact]
    public void GivenInvalidNestedResultWithNoIssues_WhenMerging_ThenSynthesizesExplanatoryError()
    {
        // Arrange
        var nestedResult = new ValidationResult(isValid: false, issues: Array.Empty<ValidationIssue>());

        // Act
        var issues = nestedResult.IssuesOrSynthesizedFailure("Patient.contact[0]", "'contact[0]' (ContactPoint)");

        // Assert
        issues.ShouldHaveSingleItem();
        issues[0].Severity.ShouldBe(IssueSeverity.Error);
        issues[0].Path.ShouldBe("Patient.contact[0]");
        issues[0].Message.ShouldContain("'contact[0]' (ContactPoint)");
    }

    [Fact]
    public void GivenInvalidNestedResultWithExistingIssues_WhenMerging_ThenDoesNotAddRedundantSynthesizedIssue()
    {
        // Arrange
        var realIssue = new ValidationIssue(
            IssueSeverity.Error,
            "cardinality-violation",
            "Patient.contact[0].name",
            "Patient.contact[0].name must have at least 1 occurrence(s), but found 0");
        var nestedResult = new ValidationResult(isValid: false, issues: new[] { realIssue });

        // Act
        var issues = nestedResult.IssuesOrSynthesizedFailure("Patient.contact[0]", "'contact[0]' (ContactPoint)");

        // Assert
        issues.ShouldHaveSingleItem();
        issues[0].ShouldBe(realIssue);
    }

    [Fact]
    public void GivenInvalidNestedResultWithOnlyAWarningIssue_WhenMerging_ThenWarningIsNotPromotedToError()
    {
        // Arrange - a directly-constructed ValidationResult that is (inconsistently) marked invalid
        // despite carrying only a Warning. Real ValidationSchema.Validate() output never carries this
        // combination (Combine derives IsValid from issue severity), but IssuesOrSynthesizedFailure
        // must not use IsValid alone to decide whether to synthesize - only "invalid and no issues at
        // all" may add an Error; an existing Warning must never be upgraded.
        var warning = new ValidationIssue(
            IssueSeverity.Warning,
            "validation-nesting-limit",
            "Patient.contact[0].extension",
            "'extension' was not validated: nesting depth limit reached");
        var nestedResult = new ValidationResult(isValid: false, issues: new[] { warning });

        // Act
        var issues = nestedResult.IssuesOrSynthesizedFailure("Patient.contact[0]", "'contact[0]' (ContactPoint)");

        // Assert
        issues.ShouldHaveSingleItem();
        issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        issues[0].ShouldBe(warning);
    }

    [Fact]
    public void GivenValidNestedResultWithNoIssues_WhenMerging_ThenReturnsEmpty()
    {
        // Arrange
        var nestedResult = ValidationResult.Success();

        // Act
        var issues = nestedResult.IssuesOrSynthesizedFailure("Patient.contact[0]", "'contact[0]' (ContactPoint)");

        // Assert
        issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenValidNestedResultWithWarnings_WhenMerging_ThenWarningsPassThroughUnchanged()
    {
        // Arrange - the case this branch exists to protect: Warnings below the resource root (e.g.
        // FhirPathInvariantCheck's engine-refusal warnings) must keep propagating untouched.
        var warning = new ValidationIssue(
            IssueSeverity.Warning,
            "validation-nesting-limit",
            "Patient.contact[0].extension",
            "'extension' was not validated: nesting depth limit reached");
        var nestedResult = new ValidationResult(isValid: true, issues: new[] { warning });

        // Act
        var issues = nestedResult.IssuesOrSynthesizedFailure("Patient.contact[0]", "'contact[0]' (ContactPoint)");

        // Assert
        issues.ShouldHaveSingleItem();
        issues[0].ShouldBe(warning);
    }
}
