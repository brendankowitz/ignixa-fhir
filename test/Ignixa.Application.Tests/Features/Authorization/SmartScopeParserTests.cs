// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Smart;

namespace Ignixa.Application.Tests.Features.Authorization;

public class SmartScopeParserTests
{
    [Theory]
    [InlineData("patient/Observation.read", SmartScopeType.Patient, "Observation", "READ")]
    [InlineData("user/Patient.write", SmartScopeType.User, "Patient", "WRITE")]
    [InlineData("system/Observation.*", SmartScopeType.System, "Observation", "*")]
    [InlineData("patient/*.read", SmartScopeType.Patient, "*", "READ")]
    [InlineData("user/*.*", SmartScopeType.User, "*", "*")]
    public void ParseScope_ValidScopes_ReturnsCorrectSmartScope(
        string scopeString,
        SmartScopeType expectedType,
        string expectedResource,
        string expectedPermission)
    {
        // Act
        var result = SmartScopeParser.ParseScope(scopeString);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(expectedType);
        result.ResourceType.Should().Be(expectedResource);
        result.Permission.Should().Be(expectedPermission);
        result.OriginalScope.Should().Be(scopeString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("patient/Observation")]
    [InlineData("patient.read")]
    [InlineData("foo/Observation.read")]
    public void ParseScope_InvalidScopes_ReturnsNull(string scopeString)
    {
        // Act
        var result = SmartScopeParser.ParseScope(scopeString);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseScopes_SpaceSeparatedString_ReturnsAllValidScopes()
    {
        // Arrange
        var scopeString = "patient/Observation.read user/Patient.write invalid system/*.read";

        // Act
        var result = SmartScopeParser.ParseScopes(scopeString);

        // Assert
        result.Should().HaveCount(3);
        result[0].Type.Should().Be(SmartScopeType.Patient);
        result[1].Type.Should().Be(SmartScopeType.User);
        result[2].Type.Should().Be(SmartScopeType.System);
    }

    [Fact]
    public void ParseScopes_EmptyString_ReturnsEmptyList()
    {
        // Act
        var result = SmartScopeParser.ParseScopes(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseScopes_NullString_ReturnsEmptyList()
    {
        // Act
        var result = SmartScopeParser.ParseScopes((string)null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("patient/Observation.read", true)]
    [InlineData("invalid", false)]
    public void IsValidSmartScope_ReturnsCorrectResult(string scope, bool expected)
    {
        // Act
        var result = SmartScopeParser.IsValidSmartScope(scope);

        // Assert
        result.Should().Be(expected);
    }
}
