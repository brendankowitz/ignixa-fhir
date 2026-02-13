// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;

namespace Ignixa.Anonymizer.Tests.Extensions;

public class ElementNavExtensionsTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    [Fact]
    public void GivenASingleResource_WhenGetResourceDescendantsWithoutSubResource_ThenDescendantsAreReturned()
    {
        // Arrange
        var json = """{"resourceType":"Patient","address":[{}],"name":[{"given":["Test"]}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);

        // Act
        var result = element.ResourceDescendantsWithoutSubResource().Select(e => e.Location).ToList();

        // Assert
        result.Count.ShouldBe(3);
        result.ShouldContain("Patient.name[0].given[0]");
        result.ShouldContain("Patient.name[0]");
        result.ShouldContain("Patient.address[0]");
    }

    [Fact]
    public void GivenAContainedNode_WhenGetResourceDescendantsWithoutSubResource_ThenContainedNodesAreExcluded()
    {
        // Arrange
        var json = """{"resourceType":"Condition","text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">Test</div>"},"contained":[{"resourceType":"Patient","address":[{}],"name":[{"given":["Test"]}]}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);

        // Act
        var result = element.ResourceDescendantsWithoutSubResource().Select(e => e.Location).ToList();

        // Assert
        result.ShouldContain("Condition.text");
        result.ShouldContain("Condition.text.div");
        result.ShouldContain("Condition.text.status");
        result.ShouldNotContain("Condition.contained[0].address[0]");
    }

    [Fact]
    public void GivenASingleResource_WhenGetSelfAndDescendantsWithoutSubResource_ThenSelfAndDescendantsAreReturned()
    {
        // Arrange
        var json = """{"resourceType":"Patient","address":[{}],"name":[{"given":["Test"]}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);

        // Act
        var testNodes = new List<IElement> { element };
        var result = testNodes.SelfAndDescendantsWithoutSubResource().Select(e => e.Location).ToList();

        // Assert
        result.Count.ShouldBe(4);
        result.ShouldContain("Patient");
        result.ShouldContain("Patient.name[0].given[0]");
        result.ShouldContain("Patient.name[0]");
        result.ShouldContain("Patient.address[0]");
    }

    [Fact]
    public void GivenAContainedNode_WhenSelfAndDescendantsWithoutSubResource_ThenContainedNodesAreExcluded()
    {
        // Arrange
        var json = """{"resourceType":"Condition","text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">Test</div>"},"contained":[{"resourceType":"Patient","address":[{}],"name":[{"given":["Test"]}]}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);

        // Act
        var testNodes = new List<IElement> { element };
        var result = testNodes.SelfAndDescendantsWithoutSubResource().Select(e => e.Location).ToList();

        // Assert
        result.ShouldContain("Condition");
        result.ShouldContain("Condition.text");
        result.ShouldContain("Condition.text.div");
        result.ShouldContain("Condition.text.status");
        result.ShouldNotContain("Condition.contained[0].address[0]");
    }
}
