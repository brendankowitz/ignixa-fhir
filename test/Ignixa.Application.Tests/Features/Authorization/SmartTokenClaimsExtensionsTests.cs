// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Smart;

namespace Ignixa.Application.Tests.Features.Authorization;

public class SmartTokenClaimsExtensionsTests
{
    #region FromScopeString Tests

    [Fact]
    public void FromScopeString_WithOpenIdScopes_SetsHasOpenIdScopeTrue()
    {
        // Arrange
        var scopeString = "openid profile patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.HasOpenIdScope.Should().BeTrue();
        claims.SpecialScopes.Should().HaveCount(2);
        claims.SpecialScopes.Should().Contain(s => s.Name == "openid");
        claims.SpecialScopes.Should().Contain(s => s.Name == "profile");
    }

    [Fact]
    public void FromScopeString_WithoutOpenIdScope_SetsHasOpenIdScopeFalse()
    {
        // Arrange
        var scopeString = "patient/Observation.rs profile email";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.HasOpenIdScope.Should().BeFalse();
    }

    [Fact]
    public void FromScopeString_WithOfflineAccess_SetsHasOfflineAccessTrue()
    {
        // Arrange
        var scopeString = "offline_access patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.HasOfflineAccess.Should().BeTrue();
        claims.SpecialScopes.Should().Contain(s => s.Name == "offline_access");
    }

    [Fact]
    public void FromScopeString_WithoutOfflineAccess_SetsHasOfflineAccessFalse()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.HasOfflineAccess.Should().BeFalse();
    }

    [Fact]
    public void FromScopeString_WithLaunchPatient_SetsLaunchContextToPatient()
    {
        // Arrange
        var scopeString = "launch/patient patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.LaunchContext.Should().Be("patient");
        claims.SpecialScopes.Should().Contain(s => s.Name == "launch/patient");
    }

    [Fact]
    public void FromScopeString_WithLaunchEncounter_SetsLaunchContextToEncounter()
    {
        // Arrange
        var scopeString = "launch/encounter patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.LaunchContext.Should().Be("encounter");
        claims.SpecialScopes.Should().Contain(s => s.Name == "launch/encounter");
    }

    [Fact]
    public void FromScopeString_WithLaunchOnly_SetsLaunchContextToNull()
    {
        // Arrange - Standalone launch with "launch" scope but no context
        var scopeString = "launch patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.LaunchContext.Should().BeNull();
        claims.SpecialScopes.Should().Contain(s => s.Name == "launch");
    }

    [Fact]
    public void FromScopeString_WithoutLaunchScopes_SetsLaunchContextToNull()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.LaunchContext.Should().BeNull();
    }

    [Fact]
    public void FromScopeString_WithAllSpecialScopes_ParsesCorrectly()
    {
        // Arrange
        var scopeString = "openid profile email fhirUser offline_access launch/patient patient/Observation.rs user/*.cruds";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.ScopeString.Should().Be(scopeString);
        claims.Scopes.Should().HaveCount(2); // FHIR resource scopes
        claims.SpecialScopes.Should().HaveCount(6); // Special scopes
        claims.HasOpenIdScope.Should().BeTrue();
        claims.HasOfflineAccess.Should().BeTrue();
        claims.LaunchContext.Should().Be("patient");
    }

    [Fact]
    public void FromScopeString_WithNullScopeString_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => SmartTokenClaimsExtensions.FromScopeString(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void GetOpenIdConnectScopes_ReturnsOnlyOidcScopes()
    {
        // Arrange
        var scopeString = "openid profile email offline_access launch/patient";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act
        var oidcScopes = claims.GetOpenIdConnectScopes().ToList();

        // Assert
        oidcScopes.Should().HaveCount(3);
        oidcScopes.Should().Contain("openid");
        oidcScopes.Should().Contain("profile");
        oidcScopes.Should().Contain("email");
        oidcScopes.Should().NotContain("offline_access");
        oidcScopes.Should().NotContain("launch/patient");
    }

    [Fact]
    public void GetLaunchScopes_ReturnsOnlyLaunchScopes()
    {
        // Arrange
        var scopeString = "openid launch launch/patient launch/encounter offline_access";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act
        var launchScopes = claims.GetLaunchScopes().ToList();

        // Assert
        launchScopes.Should().HaveCount(3);
        launchScopes.Should().Contain("launch");
        launchScopes.Should().Contain("launch/patient");
        launchScopes.Should().Contain("launch/encounter");
        launchScopes.Should().NotContain("openid");
        launchScopes.Should().NotContain("offline_access");
    }

    [Fact]
    public void HasSpecialScope_WithExistingScope_ReturnsTrue()
    {
        // Arrange
        var scopeString = "openid profile patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.HasSpecialScope("openid").Should().BeTrue();
        claims.HasSpecialScope("profile").Should().BeTrue();
        claims.HasSpecialScope("OPENID").Should().BeTrue(); // Case insensitive
    }

    [Fact]
    public void HasSpecialScope_WithNonExistingScope_ReturnsFalse()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.HasSpecialScope("profile").Should().BeFalse();
        claims.HasSpecialScope("offline_access").Should().BeFalse();
    }

    [Fact]
    public void IsEhrLaunch_WithLaunchScopes_ReturnsTrue()
    {
        // Arrange
        var scopeString = "launch/patient patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.IsEhrLaunch().Should().BeTrue();
    }

    [Fact]
    public void IsEhrLaunch_WithoutLaunchScopes_ReturnsFalse()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.IsEhrLaunch().Should().BeFalse();
    }

    [Fact]
    public void IsStandaloneLaunch_WithoutLaunchScopes_ReturnsTrue()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.IsStandaloneLaunch().Should().BeTrue();
    }

    [Fact]
    public void IsStandaloneLaunch_WithLaunchScopes_ReturnsFalse()
    {
        // Arrange
        var scopeString = "launch/patient patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.IsStandaloneLaunch().Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetOpenIdConnectScopes_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => ((SmartTokenClaims)null!).GetOpenIdConnectScopes();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetLaunchScopes_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => ((SmartTokenClaims)null!).GetLaunchScopes();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HasSpecialScope_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => ((SmartTokenClaims)null!).HasSpecialScope("openid");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HasSpecialScope_WithNullScopeName_ThrowsArgumentNullException()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        var act = () => claims.HasSpecialScope(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsEhrLaunch_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => ((SmartTokenClaims)null!).IsEhrLaunch();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsStandaloneLaunch_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => ((SmartTokenClaims)null!).IsStandaloneLaunch();
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
