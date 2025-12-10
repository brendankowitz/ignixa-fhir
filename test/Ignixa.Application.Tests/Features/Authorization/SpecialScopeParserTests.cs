// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Smart;

namespace Ignixa.Application.Tests.Features.Authorization;

public class SpecialScopeParserTests
{
    #region ParseSpecialScope Tests

    [Theory]
    [InlineData("openid", "openid", SpecialScopeType.OpenIdConnect)]
    [InlineData("profile", "profile", SpecialScopeType.OpenIdConnect)]
    [InlineData("email", "email", SpecialScopeType.OpenIdConnect)]
    [InlineData("fhirUser", "fhirUser", SpecialScopeType.OpenIdConnect)]
    [InlineData("fhiruser", "fhirUser", SpecialScopeType.OpenIdConnect)] // Case insensitive
    [InlineData("OPENID", "openid", SpecialScopeType.OpenIdConnect)] // Case insensitive
    public void ParseSpecialScope_OpenIdConnectScopes_ReturnsCorrectSpecialScope(
        string scopeString,
        string expectedName,
        SpecialScopeType expectedType)
    {
        // Act
        var result = SmartScopeParser.ParseSpecialScope(scopeString);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be(expectedName);
        result.Type.Should().Be(expectedType);
    }

    [Fact]
    public void ParseSpecialScope_OfflineAccess_ReturnsCorrectSpecialScope()
    {
        // Act
        var result = SmartScopeParser.ParseSpecialScope("offline_access");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("offline_access");
        result.Type.Should().Be(SpecialScopeType.OfflineAccess);
    }

    [Theory]
    [InlineData("launch", "launch", SpecialScopeType.Launch)]
    [InlineData("launch/patient", "launch/patient", SpecialScopeType.Launch)]
    [InlineData("launch/encounter", "launch/encounter", SpecialScopeType.Launch)]
    [InlineData("LAUNCH/PATIENT", "launch/patient", SpecialScopeType.Launch)] // Case insensitive
    public void ParseSpecialScope_LaunchScopes_ReturnsCorrectSpecialScope(
        string scopeString,
        string expectedName,
        SpecialScopeType expectedType)
    {
        // Act
        var result = SmartScopeParser.ParseSpecialScope(scopeString);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be(expectedName);
        result.Type.Should().Be(expectedType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("patient/Observation.rs")] // FHIR resource scope, not special
    [InlineData("launch/unknown")] // Invalid launch context
    [InlineData("unknown_scope")]
    public void ParseSpecialScope_InvalidScopes_ReturnsNull(string scopeString)
    {
        // Act
        var result = SmartScopeParser.ParseSpecialScope(scopeString);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region IsSpecialScope Tests

    [Theory]
    [InlineData("openid", true)]
    [InlineData("profile", true)]
    [InlineData("email", true)]
    [InlineData("fhirUser", true)]
    [InlineData("offline_access", true)]
    [InlineData("launch", true)]
    [InlineData("launch/patient", true)]
    [InlineData("launch/encounter", true)]
    [InlineData("invalid", false)]
    [InlineData("patient/Observation.rs", false)]
    public void IsSpecialScope_ReturnsCorrectResult(string scope, bool expected)
    {
        // Act
        var result = SmartScopeParser.IsSpecialScope(scope);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region ParseSpecialScopes Tests

    [Fact]
    public void ParseSpecialScopes_SpaceSeparatedString_ReturnsAllValidScopes()
    {
        // Arrange
        var scopeString = "openid profile email fhirUser offline_access launch/patient invalid patient/Observation.rs";

        // Act
        var result = SmartScopeParser.ParseSpecialScopes(scopeString);

        // Assert
        result.Should().HaveCount(6);
        result[0].Name.Should().Be("openid");
        result[0].Type.Should().Be(SpecialScopeType.OpenIdConnect);
        result[1].Name.Should().Be("profile");
        result[1].Type.Should().Be(SpecialScopeType.OpenIdConnect);
        result[2].Name.Should().Be("email");
        result[2].Type.Should().Be(SpecialScopeType.OpenIdConnect);
        result[3].Name.Should().Be("fhirUser");
        result[3].Type.Should().Be(SpecialScopeType.OpenIdConnect);
        result[4].Name.Should().Be("offline_access");
        result[4].Type.Should().Be(SpecialScopeType.OfflineAccess);
        result[5].Name.Should().Be("launch/patient");
        result[5].Type.Should().Be(SpecialScopeType.Launch);
    }

    [Fact]
    public void ParseSpecialScopes_EmptyString_ReturnsEmptyList()
    {
        // Act
        var result = SmartScopeParser.ParseSpecialScopes(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseSpecialScopes_NullString_ReturnsEmptyList()
    {
        // Act
        var result = SmartScopeParser.ParseSpecialScopes(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseSpecialScopes_OnlyInvalidScopes_ReturnsEmptyList()
    {
        // Arrange
        var scopeString = "invalid patient/Observation.rs user/*.cruds";

        // Act
        var result = SmartScopeParser.ParseSpecialScopes(scopeString);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseSpecialScopes_MixedCasing_ParsesCorrectly()
    {
        // Arrange
        var scopeString = "OpenId PROFILE Email FHIRUSER OFFLINE_ACCESS LAUNCH/PATIENT";

        // Act
        var result = SmartScopeParser.ParseSpecialScopes(scopeString);

        // Assert
        result.Should().HaveCount(6);
        result[0].Name.Should().Be("openid");
        result[1].Name.Should().Be("profile");
        result[2].Name.Should().Be("email");
        result[3].Name.Should().Be("fhirUser");
        result[4].Name.Should().Be("offline_access");
        result[5].Name.Should().Be("launch/patient");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ParseScopes_MixedSmartAndSpecialScopes_OnlyReturnsFhirResourceScopes()
    {
        // Arrange - Mix of FHIR resource scopes and special scopes
        var scopeString = "openid patient/Observation.rs profile user/Patient.cruds offline_access launch/patient";

        // Act
        var smartScopes = SmartScopeParser.ParseScopes(scopeString);
        var specialScopes = SmartScopeParser.ParseSpecialScopes(scopeString);

        // Assert - FHIR resource scopes
        smartScopes.Should().HaveCount(2);
        smartScopes[0].ResourceType.Should().Be("Observation");
        smartScopes[1].ResourceType.Should().Be("Patient");

        // Assert - Special scopes
        specialScopes.Should().HaveCount(4);
        specialScopes[0].Name.Should().Be("openid");
        specialScopes[1].Name.Should().Be("profile");
        specialScopes[2].Name.Should().Be("offline_access");
        specialScopes[3].Name.Should().Be("launch/patient");
    }

    [Fact]
    public void SpecialScope_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var scope1 = new SpecialScope("openid", SpecialScopeType.OpenIdConnect);
        var scope2 = new SpecialScope("openid", SpecialScopeType.OpenIdConnect);
        var scope3 = new SpecialScope("profile", SpecialScopeType.OpenIdConnect);

        // Assert
        scope1.Should().Be(scope2);
        scope1.Should().NotBe(scope3);
    }

    #endregion
}
