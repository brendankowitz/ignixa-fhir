// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Builders;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Unit tests for LocationBuilder.
/// Tests basic location generation with hierarchies and references.
/// </summary>
public class LocationBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Basic Building Tests

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithName_ThenCreatesLocation()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Main Clinic")
            .Build();

        // Assert
        location.Should().NotBeNull();
        location.ResourceType.Should().Be("Location");
        location.MutableNode["name"]?.GetValue<string>().Should().Be("Main Clinic");
        location.MutableNode["status"]?.GetValue<string>().Should().Be("active");
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithStatus_ThenUsesProvidedStatus()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Closed Clinic")
            .WithStatus("inactive")
            .Build();

        // Assert
        location.MutableNode["status"]?.GetValue<string>().Should().Be("inactive");
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithId_ThenUsesProvidedId()
    {
        // Arrange
        var expectedId = "location-123";

        // Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithId(expectedId)
            .WithName("Test Location")
            .Build();

        // Assert
        location.Id.Should().Be(expectedId);
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithTag_ThenIncludesTagInMeta()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Tagged Location")
            .WithTag(tag)
            .Build();

        // Assert
        location.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tags = location.MutableNode["meta"]?["tag"]?.AsArray();
        tags.Should().HaveCount(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().Should().Be(tag);
        metaTag?["system"]?.GetValue<string>().Should().Be("http://ignixa.dev/test-isolation");
    }

    [Fact]
    public void GivenLocationBuilder_WhenNoParametersProvided_ThenBuildsWithDefaults()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        location.Should().NotBeNull();
        location.ResourceType.Should().Be("Location");
        location.Id.Should().NotBeNullOrEmpty();
        location.MutableNode["status"]?.GetValue<string>().Should().Be("active");
    }

    #endregion

    #region Address Tests

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithAddress_ThenIncludesAddressInResource()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Boston Clinic")
            .WithAddress("725 Albany St", "Boston", "MA", "02118")
            .Build();

        // Assert
        location.MutableNode["address"].Should().NotBeNull();
        var address = location.MutableNode["address"]?.AsObject();

        address?["line"]?.AsArray().Should().HaveCount(1);
        address?["line"]?.AsArray()?[0]?.GetValue<string>().Should().Be("725 Albany St");
        address?["city"]?.GetValue<string>().Should().Be("Boston");
        address?["state"]?.GetValue<string>().Should().Be("MA");
        address?["postalCode"]?.GetValue<string>().Should().Be("02118");
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithoutAddress_ThenDoesNotIncludeAddress()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Virtual Location")
            .Build();

        // Assert
        location.MutableNode.TryGetPropertyValue("address", out _).Should().BeFalse();
    }

    #endregion

    #region Reference Tests

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithManagingOrganization_ThenIncludesReference()
    {
        // Arrange
        var orgId = "org-123";

        // Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Hospital Wing A")
            .WithManagingOrganization(orgId)
            .Build();

        // Assert
        location.MutableNode["managingOrganization"].Should().NotBeNull();
        var managingOrg = location.MutableNode["managingOrganization"]?.AsObject();
        managingOrg?["reference"]?.GetValue<string>().Should().Be($"Organization/{orgId}");
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithPartOf_ThenIncludesReference()
    {
        // Arrange
        var parentLocationId = "location-building";

        // Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Room 101")
            .WithPartOf(parentLocationId)
            .Build();

        // Assert
        location.MutableNode["partOf"].Should().NotBeNull();
        var partOf = location.MutableNode["partOf"]?.AsObject();
        partOf?["reference"]?.GetValue<string>().Should().Be($"Location/{parentLocationId}");
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingHierarchy_ThenCreatesValidReferences()
    {
        // Arrange - Create a building
        var building = LocationBuilder.Create(_schemaProvider)
            .WithName("Main Building")
            .Build();

        // Act - Create a floor within the building
        var floor = LocationBuilder.Create(_schemaProvider)
            .WithName("First Floor")
            .WithPartOf(building.Id!)
            .Build();

        // Create a room within the floor
        var room = LocationBuilder.Create(_schemaProvider)
            .WithName("Room 101")
            .WithPartOf(floor.Id!)
            .Build();

        // Assert
        building.MutableNode.TryGetPropertyValue("partOf", out _).Should().BeFalse();

        var floorPartOf = floor.MutableNode["partOf"]?.AsObject();
        floorPartOf?["reference"]?.GetValue<string>().Should().Be($"Location/{building.Id}");

        var roomPartOf = room.MutableNode["partOf"]?.AsObject();
        roomPartOf?["reference"]?.GetValue<string>().Should().Be($"Location/{floor.Id}");
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void GivenLocationBuilder_WhenBuildingCompleteLocation_ThenIncludesAllProperties()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var orgId = "org-456";

        // Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithId("loc-complete")
            .WithName("Complete Clinic")
            .WithStatus("active")
            .WithManagingOrganization(orgId)
            .WithAddress("100 Medical Plaza", "Seattle", "WA", "98101")
            .WithTag(tag)
            .Build();

        // Assert
        location.Id.Should().Be("loc-complete");
        location.MutableNode["name"]?.GetValue<string>().Should().Be("Complete Clinic");
        location.MutableNode["status"]?.GetValue<string>().Should().Be("active");

        var managingOrg = location.MutableNode["managingOrganization"]?.AsObject();
        managingOrg?["reference"]?.GetValue<string>().Should().Be($"Organization/{orgId}");

        var address = location.MutableNode["address"]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Seattle");

        var tags = location.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().Should().Be(tag);
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingMultipleLocations_ThenGeneratesDifferentIds()
    {
        // Arrange & Act
        var location1 = LocationBuilder.Create(_schemaProvider)
            .WithName("Clinic 1")
            .Build();

        var location2 = LocationBuilder.Create(_schemaProvider)
            .WithName("Clinic 2")
            .Build();

        // Assert
        location1.Id.Should().NotBe(location2.Id);
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithAllReferences_ThenIncludesBothReferences()
    {
        // Arrange
        var orgId = "org-789";
        var parentLocationId = "location-parent";

        // Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Child Clinic")
            .WithManagingOrganization(orgId)
            .WithPartOf(parentLocationId)
            .Build();

        // Assert
        var managingOrg = location.MutableNode["managingOrganization"]?.AsObject();
        managingOrg?["reference"]?.GetValue<string>().Should().Be($"Organization/{orgId}");

        var partOf = location.MutableNode["partOf"]?.AsObject();
        partOf?["reference"]?.GetValue<string>().Should().Be($"Location/{parentLocationId}");
    }

    #endregion

    #region Meta Tests

    [Fact]
    public void GivenLocationBuilder_WhenBuilding_ThenIncludesMetaVersionAndLastUpdated()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Test Location")
            .Build();

        // Assert
        location.MutableNode["meta"].Should().NotBeNull();
        var meta = location.MutableNode["meta"]?.AsObject();
        meta?["versionId"]?.GetValue<string>().Should().Be("1");
        meta?["lastUpdated"]?.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithEmptyName_ThenCreatesLocationWithoutName()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        location.MutableNode.TryGetPropertyValue("name", out _).Should().BeFalse();
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithSuspendedStatus_ThenUsesSuspendedStatus()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Temporarily Closed")
            .WithStatus("suspended")
            .Build();

        // Assert
        location.MutableNode["status"]?.GetValue<string>().Should().Be("suspended");
    }

    #endregion
}
