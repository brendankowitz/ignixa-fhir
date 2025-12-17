// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Ignixa.Api.Tests.Extensions;

/// <summary>
/// Tests for QueryParameterExtensions GetPrettyParameter method.
/// Validates correct parsing of the _pretty FHIR parameter.
/// </summary>
public class QueryParameterExtensionsTests
{
    [Fact]
    public void GetPrettyParameter_WithTrueValue_ReturnsTrue()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues("true") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GetPrettyParameter_WithUpperCaseTrue_ReturnsTrue()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues("TRUE") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GetPrettyParameter_WithOne_ReturnsTrue()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues("1") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GetPrettyParameter_WithFalseValue_ReturnsFalse()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues("false") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GetPrettyParameter_WithZero_ReturnsFalse()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues("0") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GetPrettyParameter_WithNoParameter_ReturnsFalse()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>());

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GetPrettyParameter_WithNoValue_ReturnsTrue()
    {
        // Arrange - simulate ?_pretty with no value
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues("") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeTrue("FHIR spec says presence implies true");
    }

    [Fact]
    public void GetPrettyParameter_WithWhitespaceValue_ReturnsFalse()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues("   ") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GetPrettyParameter_WithInvalidValue_ReturnsFalse()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues("invalid") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GetPrettyParameter_WithMultipleValues_UsesFirstValue()
    {
        // Arrange
        string[] values = ["true", "false"];
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_pretty", new StringValues(values) }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GetPrettyParameter_WithOtherParameters_IgnoresThem()
    {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "_count", new StringValues("10") },
            { "name", new StringValues("Smith") },
            { "_pretty", new StringValues("true") }
        });

        // Act
        var result = query.GetPrettyParameter();

        // Assert
        result.ShouldBeTrue();
    }
}
