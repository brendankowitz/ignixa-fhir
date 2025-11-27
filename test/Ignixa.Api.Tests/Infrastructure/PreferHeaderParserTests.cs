// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Api.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// Unit tests for PreferHeaderParser.
/// Tests parsing of Prefer header validation level preferences.
/// </summary>
public class PreferHeaderParserTests
{
    #region Valid Preference Values

    [Fact]
    public void GivenValidationNonePreference_WhenParsing_ThenReturnsMinimal()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=none" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationDepth.Minimal);
    }

    [Fact]
    public void GivenValidationMinimumPreference_WhenParsing_ThenReturnsMinimal()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=minimum" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationDepth.Minimal);
    }

    [Fact]
    public void GivenValidationSpecPreference_WhenParsing_ThenReturnsSpec()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=spec" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationDepth.Spec);
    }

    [Fact]
    public void GivenValidationFullPreference_WhenParsing_ThenReturnsFull()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=full" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationDepth.Full);
    }

    #endregion

    #region Case Insensitivity

    [Fact]
    public void GivenUpperCaseValidationPreference_WhenParsing_ThenReturnsCorrectDepth()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "VALIDATION=SPEC" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationDepth.Spec);
    }

    [Fact]
    public void GivenMixedCaseValidationValue_WhenParsing_ThenReturnsCorrectDepth()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "Validation=Full" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationDepth.Full);
    }

    #endregion

    #region Multiple Preferences

    [Fact]
    public void GivenMultiplePreferences_WhenParsing_ThenReturnsValidationLevel()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "return=representation, validation=spec" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationDepth.Spec);
    }

    [Fact]
    public void GivenValidationNotFirst_WhenParsing_ThenReturnsValidationLevel()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "handling=lenient, validation=none, return=minimal" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationDepth.Minimal);
    }

    #endregion

    #region Invalid/Missing Headers

    [Fact]
    public void GivenNoPreferHeader_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var headers = new HeaderDictionary();

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenEmptyPreferHeader_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenWhitespacePreferHeader_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "   " } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenInvalidValidationValue_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=invalid" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenValidationWithNoValue_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenOnlyOtherPreferences_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "return=representation, handling=lenient" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Preference-Applied Header Generation

    [Fact]
    public void GivenMinimalDepth_WhenConvertingToHeader_ThenReturnsValidationMinimum()
    {
        // Act
        var result = PreferHeaderParser.ToPreferenceAppliedHeader(ValidationDepth.Minimal);

        // Assert
        result.Should().Be("validation=minimal");
    }

    [Fact]
    public void GivenSpecDepth_WhenConvertingToHeader_ThenReturnsValidationSpec()
    {
        // Act
        var result = PreferHeaderParser.ToPreferenceAppliedHeader(ValidationDepth.Spec);

        // Assert
        result.Should().Be("validation=spec");
    }

    [Fact]
    public void GivenFullDepth_WhenConvertingToHeader_ThenReturnsValidationFull()
    {
        // Act
        var result = PreferHeaderParser.ToPreferenceAppliedHeader(ValidationDepth.Full);

        // Assert
        result.Should().Be("validation=full");
    }

    #endregion
}
