// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Models;

namespace Ignixa.Application.Tests.Features.Authorization;

public class ResourceGrantTests
{
    [Fact]
    public void All_GrantsAllPermissions()
    {
        // Arrange
        var grant = ResourceGrant.All;
        var required = new ResourceGrant("Patient", "read");

        // Act
        var result = grant.Matches(required);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ExactMatch_ReturnsTrue()
    {
        // Arrange
        var grant = new ResourceGrant("Patient", "read");
        var required = new ResourceGrant("Patient", "read");

        // Act
        var result = grant.Matches(required);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void WildcardResource_MatchesAnyResource()
    {
        // Arrange
        var grant = new ResourceGrant("*", "read");
        var required = new ResourceGrant("Observation", "read");

        // Act
        var result = grant.Matches(required);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void WildcardInteraction_MatchesAnyInteraction()
    {
        // Arrange
        var grant = new ResourceGrant("Patient", "*");
        var required = new ResourceGrant("Patient", "delete");

        // Act
        var result = grant.Matches(required);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DifferentResource_ReturnsFalse()
    {
        // Arrange
        var grant = new ResourceGrant("Patient", "read");
        var required = new ResourceGrant("Observation", "read");

        // Act
        var result = grant.Matches(required);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void DifferentInteraction_ReturnsFalse()
    {
        // Arrange
        var grant = new ResourceGrant("Patient", "read");
        var required = new ResourceGrant("Patient", "delete");

        // Act
        var result = grant.Matches(required);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ReadOnly_CreatesReadPermission()
    {
        // Act
        var grant = ResourceGrant.ReadOnly("Patient");

        // Assert
        grant.ResourceType.Should().Be("Patient");
        grant.Interaction.Should().Be("read");
    }

    [Fact]
    public void FullAccess_CreatesWildcardInteraction()
    {
        // Act
        var grant = ResourceGrant.FullAccess("Patient");

        // Assert
        grant.ResourceType.Should().Be("Patient");
        grant.Interaction.Should().Be("*");
    }

    [Fact]
    public void GlobalReadOnly_CreatesGlobalReadPermission()
    {
        // Act
        var grant = ResourceGrant.GlobalReadOnly();

        // Assert
        grant.ResourceType.Should().Be("*");
        grant.Interaction.Should().Be("read");
    }

    [Fact]
    public void CaseInsensitive_ResourceMatch()
    {
        // Arrange
        var grant = new ResourceGrant("patient", "read");
        var required = new ResourceGrant("Patient", "read");

        // Act
        var result = grant.Matches(required);

        // Assert
        result.Should().BeTrue();
    }
}
