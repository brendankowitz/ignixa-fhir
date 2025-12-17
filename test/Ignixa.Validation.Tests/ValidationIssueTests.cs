// <copyright file="ValidationIssueTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Shouldly;
using Ignixa.Validation;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Unit tests for ValidationIssue.
/// </summary>
public class ValidationIssueTests
{
    [Fact]
    public void GivenValidParameters_WhenCreatingValidationIssue_ThenPropertiesAreSet()
    {
        // Arrange & Act
        var issue = new ValidationIssue(
            IssueSeverity.Error,
            "Patient.name",
            "Name is required");

        // Assert
        issue.Severity.ShouldBe(IssueSeverity.Error);
        issue.Path.ShouldBe("Patient.name");
        issue.Message.ShouldBe("Name is required");
    }

    [Fact]
    public void GivenNullPath_WhenCreatingValidationIssue_ThenThrowsArgumentNullException()
    {
        // Act
        var act = () => new ValidationIssue(IssueSeverity.Error, null!, "message");

        // Assert
        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("path");
    }

    [Fact]
    public void GivenNullMessage_WhenCreatingValidationIssue_ThenThrowsArgumentNullException()
    {
        // Act
        var act = () => new ValidationIssue(IssueSeverity.Error, "path", null!);

        // Assert
        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("message");
    }

    [Theory]
    [InlineData(IssueSeverity.Information)]
    [InlineData(IssueSeverity.Warning)]
    [InlineData(IssueSeverity.Error)]
    [InlineData(IssueSeverity.Fatal)]
    public void GivenDifferentSeverities_WhenCreatingValidationIssue_ThenSeverityIsPreserved(IssueSeverity severity)
    {
        // Arrange & Act
        var issue = new ValidationIssue(severity, "path", "message");

        // Assert
        issue.Severity.ShouldBe(severity);
    }
}
