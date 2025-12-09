// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Smart;

namespace Ignixa.Application.Tests.Features.Authorization;

public class SmartScopeTests
{
    [Fact]
    public void MatchesResource_WildcardScope_MatchesAnyResource()
    {
        // Arrange - SMART v2 format
        var scope = new SmartScope
        {
            Type = SmartScopeType.Patient,
            ResourceType = "*",
            Permissions = SmartPermissions.Read | SmartPermissions.Search,
            PermissionString = "RS",
            OriginalScope = "patient/*.rs"
        };

        // Act & Assert
        scope.MatchesResource("Observation").Should().BeTrue();
        scope.MatchesResource("Patient").Should().BeTrue();
    }

    [Fact]
    public void MatchesResource_SpecificResource_MatchesExactly()
    {
        // Arrange
        var scope = new SmartScope
        {
            Type = SmartScopeType.Patient,
            ResourceType = "Observation",
            Permissions = SmartPermissions.Read | SmartPermissions.Search,
            PermissionString = "RS",
            OriginalScope = "patient/Observation.rs"
        };

        // Act & Assert
        scope.MatchesResource("Observation").Should().BeTrue();
        scope.MatchesResource("Patient").Should().BeFalse();
    }

    [Fact]
    public void MatchesResource_NullResource_OnlyMatchesWildcard()
    {
        // Arrange
        var wildcardScope = new SmartScope { Type = SmartScopeType.System, ResourceType = "*", Permissions = SmartPermissions.Read, PermissionString = "R", OriginalScope = "system/*.r" };
        var specificScope = new SmartScope { Type = SmartScopeType.System, ResourceType = "Patient", Permissions = SmartPermissions.Read, PermissionString = "R", OriginalScope = "system/Patient.r" };

        // Act & Assert
        wildcardScope.MatchesResource(null).Should().BeTrue();
        specificScope.MatchesResource(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(SmartPermissions.All, "read", true)]
    [InlineData(SmartPermissions.All, "create", true)]
    [InlineData(SmartPermissions.All, "update", true)]
    [InlineData(SmartPermissions.All, "delete", true)]
    [InlineData(SmartPermissions.All, "search-type", true)]
    [InlineData(SmartPermissions.Read, "read", true)]
    [InlineData(SmartPermissions.Read, "create", false)]
    [InlineData(SmartPermissions.Create | SmartPermissions.Update | SmartPermissions.Delete, "create", true)]
    [InlineData(SmartPermissions.Create | SmartPermissions.Update | SmartPermissions.Delete, "update", true)]
    [InlineData(SmartPermissions.Create | SmartPermissions.Update | SmartPermissions.Delete, "delete", true)]
    [InlineData(SmartPermissions.Create | SmartPermissions.Update | SmartPermissions.Delete, "read", false)]
    [InlineData(SmartPermissions.Create, "create", true)]
    [InlineData(SmartPermissions.Read, "vread", true)]
    [InlineData(SmartPermissions.Update, "patch", true)]
    [InlineData(SmartPermissions.Search, "search-type", true)]
    public void MatchesInteraction_VariousPermissions_ReturnsCorrectResult(
        SmartPermissions scopePermissions,
        string requiredInteraction,
        bool expected)
    {
        // Arrange
        var scope = new SmartScope
        {
            Type = SmartScopeType.Patient,
            ResourceType = "Patient",
            Permissions = scopePermissions,
            PermissionString = "TEST",
            OriginalScope = "patient/Patient.test"
        };

        // Act
        var result = scope.MatchesInteraction(requiredInteraction);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Matches_BothResourceAndInteraction_ReturnsCorrectResult()
    {
        // Arrange
        var scope = new SmartScope
        {
            Type = SmartScopeType.Patient,
            ResourceType = "Observation",
            Permissions = SmartPermissions.Read | SmartPermissions.Search,
            PermissionString = "RS",
            OriginalScope = "patient/Observation.rs"
        };

        // Act & Assert
        scope.Matches("Observation", "read").Should().BeTrue();
        scope.Matches("Observation", "search-type").Should().BeTrue();
        scope.Matches("Observation", "create").Should().BeFalse();
        scope.Matches("Patient", "read").Should().BeFalse();
    }

    [Fact]
    public void HasPermission_ChecksFlags_Correctly()
    {
        // Arrange
        var scope = new SmartScope
        {
            Type = SmartScopeType.User,
            ResourceType = "Patient",
            Permissions = SmartPermissions.Create | SmartPermissions.Read | SmartPermissions.Update,
            PermissionString = "CRU",
            OriginalScope = "user/Patient.cru"
        };

        // Act & Assert
        scope.HasPermission(SmartPermissions.Create).Should().BeTrue();
        scope.HasPermission(SmartPermissions.Read).Should().BeTrue();
        scope.HasPermission(SmartPermissions.Update).Should().BeTrue();
        scope.HasPermission(SmartPermissions.Delete).Should().BeFalse();
        scope.HasPermission(SmartPermissions.Search).Should().BeFalse();
    }

    [Fact]
    public void PractitionerScope_WorksCorrectly()
    {
        // Arrange - SMART v2 Practitioner scope
        var scope = new SmartScope
        {
            Type = SmartScopeType.Practitioner,
            ResourceType = "Schedule",
            Permissions = SmartPermissions.Read | SmartPermissions.Search,
            PermissionString = "RS",
            OriginalScope = "practitioner/Schedule.rs"
        };

        // Act & Assert
        scope.Type.Should().Be(SmartScopeType.Practitioner);
        scope.MatchesResource("Schedule").Should().BeTrue();
        scope.MatchesInteraction("read").Should().BeTrue();
    }
}
