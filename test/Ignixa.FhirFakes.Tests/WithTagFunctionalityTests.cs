// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for WithTag() functionality in SchemaBasedFhirResourceFaker.
/// Verifies that resources can be tagged for test isolation using the _tag search parameter.
/// </summary>
public class WithTagFunctionalityTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly SchemaBasedFhirResourceFaker _faker;

    public WithTagFunctionalityTests()
    {
        _faker = new SchemaBasedFhirResourceFaker(_schemaProvider);
    }

    [Fact]
    public void GivenFakerWithoutTag_WhenGeneratingResource_ThenResourceHasNoTagArray()
    {
        // Act
        var patient = _faker.Generate("Patient");

        // Assert
        patient.MutableNode["meta"].Should().NotBeNull();
        patient.MutableNode["meta"]!["tag"].Should().BeNull();
    }

    [Fact]
    public void GivenFakerWithTag_WhenGeneratingResource_ThenResourceHasTagWithCode()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();
        _faker.WithTag(tagCode);

        // Act
        var patient = _faker.Generate("Patient");

        // Assert
        patient.MutableNode["meta"].Should().NotBeNull();
        var meta = patient.MutableNode["meta"]!.AsObject();
        meta["tag"].Should().NotBeNull();

        var tagArray = meta["tag"]!.AsArray();
        tagArray.Should().HaveCount(1);

        var tag = tagArray[0]!.AsObject();
        tag["code"].Should().NotBeNull();
        tag["code"]!.GetValue<string>().Should().Be(tagCode);
    }

    [Fact]
    public void GivenFakerWithTag_WhenGeneratingMultipleResources_ThenAllResourcesHaveSameTag()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();
        _faker.WithTag(tagCode);

        // Act
        var patient1 = _faker.Generate("Patient");
        var patient2 = _faker.Generate("Patient");
        var observation = _faker.Generate("Observation");

        // Assert - All resources should have the same tag
        var getTagCode = (JsonNode resource) =>
            resource["meta"]!["tag"]![0]!["code"]!.GetValue<string>();

        getTagCode(patient1.MutableNode).Should().Be(tagCode);
        getTagCode(patient2.MutableNode).Should().Be(tagCode);
        getTagCode(observation.MutableNode).Should().Be(tagCode);
    }

    [Fact]
    public void GivenFakerWithTag_WhenChangingTag_ThenNewResourcesGetNewTag()
    {
        // Arrange
        var tagCode1 = "test-tag-1";
        var tagCode2 = "test-tag-2";

        // Act - Generate with first tag
        _faker.WithTag(tagCode1);
        var patient1 = _faker.Generate("Patient");

        // Act - Change tag and generate again
        _faker.WithTag(tagCode2);
        var patient2 = _faker.Generate("Patient");

        // Assert
        var getTagCode = (JsonNode resource) =>
            resource["meta"]!["tag"]![0]!["code"]!.GetValue<string>();

        getTagCode(patient1.MutableNode).Should().Be(tagCode1);
        getTagCode(patient2.MutableNode).Should().Be(tagCode2);
    }

    [Fact]
    public void GivenFakerWithTag_WhenSettingTagToNull_ThenNewResourcesHaveNoTag()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();
        _faker.WithTag(tagCode);
        var patient1 = _faker.Generate("Patient");

        // Act - Clear tag
        _faker.WithTag(null);
        var patient2 = _faker.Generate("Patient");

        // Assert
        patient1.MutableNode["meta"]!["tag"].Should().NotBeNull();
        patient2.MutableNode["meta"]!["tag"].Should().BeNull();
    }

    [Fact]
    public void GivenFakerWithTag_WhenUsingFluentChaining_ThenReturnsCorrectInstance()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();

        // Act - Use fluent chaining
        var result = _faker.WithTag(tagCode);

        // Assert
        result.Should().BeSameAs(_faker);
    }

    [Fact]
    public void GivenFakerWithTag_WhenResourceAlreadyHasMeta_ThenTagIsAddedToExistingMeta()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();
        _faker.WithTag(tagCode);

        // Act
        var patient = _faker.Generate("Patient");

        // Assert - Meta should have both the existing properties (versionId, lastUpdated) and the new tag
        var meta = patient.MutableNode["meta"]!.AsObject();
        meta["versionId"].Should().NotBeNull();
        meta["lastUpdated"].Should().NotBeNull();
        meta["tag"].Should().NotBeNull();

        var tagArray = meta["tag"]!.AsArray();
        tagArray.Should().HaveCount(1);
        tagArray[0]!["code"]!.GetValue<string>().Should().Be(tagCode);
    }
}
