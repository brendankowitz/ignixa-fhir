// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;
using Ignixa.Serialization.SourceNodes;

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
        location.ShouldNotBeNull();
        location.ResourceType.ShouldBe("Location");
        ((IMutableJsonNode)location).MutableNode["name"]?.GetValue<string>().ShouldBe("Main Clinic");
        ((IMutableJsonNode)location).MutableNode["status"]?.GetValue<string>().ShouldBe("active");
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
        ((IMutableJsonNode)location).MutableNode["status"]?.GetValue<string>().ShouldBe("inactive");
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
        location.Id.ShouldBe(expectedId);
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
        ((IMutableJsonNode)location).MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tags = ((IMutableJsonNode)location).MutableNode["meta"]?["tag"]?.AsArray();
        tags!.Count.ShouldBe(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().ShouldBe(tag);
        metaTag?["system"]?.GetValue<string>().ShouldBe("http://ignixa.io/fhir/CodeSystem/test-isolation");
    }

    [Fact]
    public void GivenLocationBuilder_WhenNoParametersProvided_ThenBuildsWithDefaults()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        location.ShouldNotBeNull();
        location.ResourceType.ShouldBe("Location");
        location.Id.ShouldNotBeNullOrEmpty();
        ((IMutableJsonNode)location).MutableNode["status"]?.GetValue<string>().ShouldBe("active");
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
        ((IMutableJsonNode)location).MutableNode["address"].ShouldNotBeNull();
        var address = ((IMutableJsonNode)location).MutableNode["address"]?.AsObject();

        address?["line"]?.AsArray().Count.ShouldBe(1);
        address?["line"]?.AsArray()?[0]?.GetValue<string>().ShouldBe("725 Albany St");
        address?["city"]?.GetValue<string>().ShouldBe("Boston");
        address?["state"]?.GetValue<string>().ShouldBe("MA");
        address?["postalCode"]?.GetValue<string>().ShouldBe("02118");
    }

    [Fact]
    public void GivenLocationBuilder_WhenBuildingWithoutAddress_ThenDoesNotIncludeAddress()
    {
        // Arrange & Act
        var location = LocationBuilder.Create(_schemaProvider)
            .WithName("Virtual Location")
            .Build();

        // Assert
        ((IMutableJsonNode)location).MutableNode.TryGetPropertyValue("address", out _).ShouldBeFalse();
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
        ((IMutableJsonNode)location).MutableNode["managingOrganization"].ShouldNotBeNull();
        var managingOrg = ((IMutableJsonNode)location).MutableNode["managingOrganization"]?.AsObject();
        managingOrg?["reference"]?.GetValue<string>().ShouldBe($"Organization/{orgId}");
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
        ((IMutableJsonNode)location).MutableNode["partOf"].ShouldNotBeNull();
        var partOf = ((IMutableJsonNode)location).MutableNode["partOf"]?.AsObject();
        partOf?["reference"]?.GetValue<string>().ShouldBe($"Location/{parentLocationId}");
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
        ((IMutableJsonNode)building).MutableNode.TryGetPropertyValue("partOf", out _).ShouldBeFalse();

        var floorPartOf = ((IMutableJsonNode)floor).MutableNode["partOf"]?.AsObject();
        floorPartOf?["reference"]?.GetValue<string>().ShouldBe($"Location/{building.Id}");

        var roomPartOf = ((IMutableJsonNode)room).MutableNode["partOf"]?.AsObject();
        roomPartOf?["reference"]?.GetValue<string>().ShouldBe($"Location/{floor.Id}");
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
        location.Id.ShouldBe("loc-complete");
        ((IMutableJsonNode)location).MutableNode["name"]?.GetValue<string>().ShouldBe("Complete Clinic");
        ((IMutableJsonNode)location).MutableNode["status"]?.GetValue<string>().ShouldBe("active");

        var managingOrg = ((IMutableJsonNode)location).MutableNode["managingOrganization"]?.AsObject();
        managingOrg?["reference"]?.GetValue<string>().ShouldBe($"Organization/{orgId}");

        var address = ((IMutableJsonNode)location).MutableNode["address"]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Seattle");

        var tags = ((IMutableJsonNode)location).MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().ShouldBe(tag);
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
        location1.Id.ShouldNotBe(location2.Id);
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
        var managingOrg = ((IMutableJsonNode)location).MutableNode["managingOrganization"]?.AsObject();
        managingOrg?["reference"]?.GetValue<string>().ShouldBe($"Organization/{orgId}");

        var partOf = ((IMutableJsonNode)location).MutableNode["partOf"]?.AsObject();
        partOf?["reference"]?.GetValue<string>().ShouldBe($"Location/{parentLocationId}");
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
        ((IMutableJsonNode)location).MutableNode["meta"].ShouldNotBeNull();
        var meta = ((IMutableJsonNode)location).MutableNode["meta"]?.AsObject();
        meta?["versionId"]?.GetValue<string>().ShouldBe("1");
        meta?["lastUpdated"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
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
        ((IMutableJsonNode)location).MutableNode.TryGetPropertyValue("name", out _).ShouldBeFalse();
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
        ((IMutableJsonNode)location).MutableNode["status"]?.GetValue<string>().ShouldBe("suspended");
    }

    #endregion
}
