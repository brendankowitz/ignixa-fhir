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
        // Arrange
        var scope = new SmartScope
        {
            Type = SmartScopeType.Patient,
            ResourceType = "*",
            Permission = "READ",
            OriginalScope = "patient/*.read"
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
            Permission = "READ",
            OriginalScope = "patient/Observation.read"
        };

        // Act & Assert
        scope.MatchesResource("Observation").Should().BeTrue();
        scope.MatchesResource("Patient").Should().BeFalse();
    }

    [Fact]
    public void MatchesResource_NullResource_OnlyMatchesWildcard()
    {
        // Arrange
        var wildcardScope = new SmartScope { Type = SmartScopeType.System, ResourceType = "*", Permission = "READ", OriginalScope = "system/*.read" };
        var specificScope = new SmartScope { Type = SmartScopeType.System, ResourceType = "Patient", Permission = "READ", OriginalScope = "system/Patient.read" };

        // Act & Assert
        wildcardScope.MatchesResource(null).Should().BeTrue();
        specificScope.MatchesResource(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("*", "read", true)]
    [InlineData("*", "create", true)]
    [InlineData("*", "update", true)]
    [InlineData("*", "delete", true)]
    [InlineData("READ", "read", true)]
    [InlineData("READ", "create", false)]
    [InlineData("WRITE", "create", true)]
    [InlineData("WRITE", "update", true)]
    [InlineData("WRITE", "delete", true)]
    [InlineData("WRITE", "read", false)]
    [InlineData("C", "create", true)]
    [InlineData("R", "read", true)]
    [InlineData("U", "update", true)]
    [InlineData("D", "delete", true)]
    public void MatchesPermission_VariousPermissions_ReturnsCorrectResult(
        string scopePermission,
        string requiredPermission,
        bool expected)
    {
        // Arrange
        var scope = new SmartScope
        {
            Type = SmartScopeType.Patient,
            ResourceType = "Patient",
            Permission = scopePermission,
            OriginalScope = $"patient/Patient.{scopePermission.ToUpperInvariant()}"
        };

        // Act
        var result = scope.MatchesPermission(requiredPermission);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Matches_BothResourceAndPermission_ReturnsCorrectResult()
    {
        // Arrange
        var scope = new SmartScope
        {
            Type = SmartScopeType.Patient,
            ResourceType = "Observation",
            Permission = "READ",
            OriginalScope = "patient/Observation.read"
        };

        // Act & Assert
        scope.Matches("Observation", "read").Should().BeTrue();
        scope.Matches("Observation", "create").Should().BeFalse();
        scope.Matches("Patient", "read").Should().BeFalse();
    }
}
