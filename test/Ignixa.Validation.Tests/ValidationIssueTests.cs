// <copyright file="ValidationIssueTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using FluentAssertions;
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
        issue.Severity.Should().Be(IssueSeverity.Error);
        issue.Path.Should().Be("Patient.name");
        issue.Message.Should().Be("Name is required");
    }

    [Fact]
    public void GivenNullPath_WhenCreatingValidationIssue_ThenThrowsArgumentNullException()
    {
        // Act
        var act = () => new ValidationIssue(IssueSeverity.Error, null!, "message");

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("path");
    }

    [Fact]
    public void GivenNullMessage_WhenCreatingValidationIssue_ThenThrowsArgumentNullException()
    {
        // Act
        var act = () => new ValidationIssue(IssueSeverity.Error, "path", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("message");
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
        issue.Severity.Should().Be(severity);
    }
}
