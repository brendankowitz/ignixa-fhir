// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.Infrastructure;
using Ignixa.Validation;
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
    public void GivenValidationNonePreference_WhenParsing_ThenReturnsNone()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=none" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationTier.None);
    }

    [Fact]
    public void GivenValidationMinimumPreference_WhenParsing_ThenReturnsFast()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=minimum" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationTier.Fast);
    }

    [Fact]
    public void GivenValidationSpecPreference_WhenParsing_ThenReturnsSpec()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=spec" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationTier.Spec);
    }

    [Fact]
    public void GivenValidationFullPreference_WhenParsing_ThenReturnsProfile()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "validation=full" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationTier.Profile);
    }

    #endregion

    #region Case Insensitivity

    [Fact]
    public void GivenUpperCaseValidationPreference_WhenParsing_ThenReturnsCorrectTier()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "VALIDATION=SPEC" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationTier.Spec);
    }

    [Fact]
    public void GivenMixedCaseValidationValue_WhenParsing_ThenReturnsCorrectTier()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "Validation=Full" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationTier.Profile);
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
        result.Should().Be(ValidationTier.Spec);
    }

    [Fact]
    public void GivenValidationNotFirst_WhenParsing_ThenReturnsValidationLevel()
    {
        // Arrange
        var headers = new HeaderDictionary { { "Prefer", "handling=lenient, validation=none, return=minimal" } };

        // Act
        var result = PreferHeaderParser.TryParseValidationLevel(headers);

        // Assert
        result.Should().Be(ValidationTier.None);
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
    public void GivenNoneTier_WhenConvertingToHeader_ThenReturnsValidationNone()
    {
        // Act
        var result = PreferHeaderParser.ToPreferenceAppliedHeader(ValidationTier.None);

        // Assert
        result.Should().Be("validation=none");
    }

    [Fact]
    public void GivenFastTier_WhenConvertingToHeader_ThenReturnsValidationMinimum()
    {
        // Act
        var result = PreferHeaderParser.ToPreferenceAppliedHeader(ValidationTier.Fast);

        // Assert
        result.Should().Be("validation=minimum");
    }

    [Fact]
    public void GivenSpecTier_WhenConvertingToHeader_ThenReturnsValidationSpec()
    {
        // Act
        var result = PreferHeaderParser.ToPreferenceAppliedHeader(ValidationTier.Spec);

        // Assert
        result.Should().Be("validation=spec");
    }

    [Fact]
    public void GivenProfileTier_WhenConvertingToHeader_ThenReturnsValidationFull()
    {
        // Act
        var result = PreferHeaderParser.ToPreferenceAppliedHeader(ValidationTier.Profile);

        // Assert
        result.Should().Be("validation=full");
    }

    #endregion
}
