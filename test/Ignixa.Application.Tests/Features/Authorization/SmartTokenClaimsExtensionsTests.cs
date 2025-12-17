// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
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
        claims.HasOpenIdScope.ShouldBeTrue();
        claims.SpecialScopes.Count.ShouldBe(2);
        claims.SpecialScopes.ShouldContain(s => s.Name == "openid");
        claims.SpecialScopes.ShouldContain(s => s.Name == "profile");
    }

    [Fact]
    public void FromScopeString_WithoutOpenIdScope_SetsHasOpenIdScopeFalse()
    {
        // Arrange
        var scopeString = "patient/Observation.rs profile email";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.HasOpenIdScope.ShouldBeFalse();
    }

    [Fact]
    public void FromScopeString_WithOfflineAccess_SetsHasOfflineAccessTrue()
    {
        // Arrange
        var scopeString = "offline_access patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.HasOfflineAccess.ShouldBeTrue();
        claims.SpecialScopes.ShouldContain(s => s.Name == "offline_access");
    }

    [Fact]
    public void FromScopeString_WithoutOfflineAccess_SetsHasOfflineAccessFalse()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.HasOfflineAccess.ShouldBeFalse();
    }

    [Fact]
    public void FromScopeString_WithLaunchPatient_SetsLaunchContextToPatient()
    {
        // Arrange
        var scopeString = "launch/patient patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.LaunchContext.ShouldBe("patient");
        claims.SpecialScopes.ShouldContain(s => s.Name == "launch/patient");
    }

    [Fact]
    public void FromScopeString_WithLaunchEncounter_SetsLaunchContextToEncounter()
    {
        // Arrange
        var scopeString = "launch/encounter patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.LaunchContext.ShouldBe("encounter");
        claims.SpecialScopes.ShouldContain(s => s.Name == "launch/encounter");
    }

    [Fact]
    public void FromScopeString_WithLaunchOnly_SetsLaunchContextToNull()
    {
        // Arrange - Standalone launch with "launch" scope but no context
        var scopeString = "launch patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.LaunchContext.ShouldBeNull();
        claims.SpecialScopes.ShouldContain(s => s.Name == "launch");
    }

    [Fact]
    public void FromScopeString_WithoutLaunchScopes_SetsLaunchContextToNull()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.LaunchContext.ShouldBeNull();
    }

    [Fact]
    public void FromScopeString_WithAllSpecialScopes_ParsesCorrectly()
    {
        // Arrange
        var scopeString = "openid profile email fhirUser offline_access launch/patient patient/Observation.rs user/*.cruds";

        // Act
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Assert
        claims.ScopeString.ShouldBe(scopeString);
        claims.Scopes.Count.ShouldBe(2); // FHIR resource scopes
        claims.SpecialScopes.Count.ShouldBe(6); // Special scopes
        claims.HasOpenIdScope.ShouldBeTrue();
        claims.HasOfflineAccess.ShouldBeTrue();
        claims.LaunchContext.ShouldBe("patient");
    }

    [Fact]
    public void FromScopeString_WithNullScopeString_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => SmartTokenClaimsExtensions.FromScopeString(null!);
        Should.Throw<ArgumentNullException>(act);
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
        oidcScopes.Count.ShouldBe(3);
        oidcScopes.ShouldContain("openid");
        oidcScopes.ShouldContain("profile");
        oidcScopes.ShouldContain("email");
        oidcScopes.ShouldNotContain("offline_access");
        oidcScopes.ShouldNotContain("launch/patient");
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
        launchScopes.Count.ShouldBe(3);
        launchScopes.ShouldContain("launch");
        launchScopes.ShouldContain("launch/patient");
        launchScopes.ShouldContain("launch/encounter");
        launchScopes.ShouldNotContain("openid");
        launchScopes.ShouldNotContain("offline_access");
    }

    [Fact]
    public void HasSpecialScope_WithExistingScope_ReturnsTrue()
    {
        // Arrange
        var scopeString = "openid profile patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.HasSpecialScope("openid").ShouldBeTrue();
        claims.HasSpecialScope("profile").ShouldBeTrue();
        claims.HasSpecialScope("OPENID").ShouldBeTrue(); // Case insensitive
    }

    [Fact]
    public void HasSpecialScope_WithNonExistingScope_ReturnsFalse()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.HasSpecialScope("profile").ShouldBeFalse();
        claims.HasSpecialScope("offline_access").ShouldBeFalse();
    }

    [Fact]
    public void IsEhrLaunch_WithLaunchScopes_ReturnsTrue()
    {
        // Arrange
        var scopeString = "launch/patient patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.IsEhrLaunch().ShouldBeTrue();
    }

    [Fact]
    public void IsEhrLaunch_WithoutLaunchScopes_ReturnsFalse()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.IsEhrLaunch().ShouldBeFalse();
    }

    [Fact]
    public void IsStandaloneLaunch_WithoutLaunchScopes_ReturnsTrue()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.IsStandaloneLaunch().ShouldBeTrue();
    }

    [Fact]
    public void IsStandaloneLaunch_WithLaunchScopes_ReturnsFalse()
    {
        // Arrange
        var scopeString = "launch/patient patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        claims.IsStandaloneLaunch().ShouldBeFalse();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetOpenIdConnectScopes_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => ((SmartTokenClaims)null!).GetOpenIdConnectScopes();
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void GetLaunchScopes_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => ((SmartTokenClaims)null!).GetLaunchScopes();
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void HasSpecialScope_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => { _ = ((SmartTokenClaims)null!).HasSpecialScope("openid"); });
    }

    [Fact]
    public void HasSpecialScope_WithNullScopeName_ThrowsArgumentNullException()
    {
        // Arrange
        var scopeString = "openid patient/Observation.rs";
        var claims = SmartTokenClaimsExtensions.FromScopeString(scopeString);

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => { _ = claims.HasSpecialScope(null!); });
    }

    [Fact]
    public void IsEhrLaunch_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => { _ = ((SmartTokenClaims)null!).IsEhrLaunch(); });
    }

    [Fact]
    public void IsStandaloneLaunch_WithNullClaims_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => { _ = ((SmartTokenClaims)null!).IsStandaloneLaunch(); });
    }

    #endregion
}
