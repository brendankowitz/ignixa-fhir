// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.DeId.Models;

namespace Ignixa.DeId.Tests.Models;

public class EmptyElementTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static IEnumerable<object[]> EmptyElementFiles()
    {
        yield return ["patient-empty.json"];
        yield return ["bundle-empty.json"];
        yield return ["condition-empty.json"];
    }

    public static IEnumerable<object[]> NonEmptyElementFiles()
    {
        yield return ["contained-basic.json"];
        yield return ["bundle-basic.json"];
    }

    public static IEnumerable<object[]> NonEmptyElementContent()
    {
        yield return [null!];
        yield return ["0"];
        yield return ["empty"];
        yield return ["""{"resourceType":"Patient"}"""];
    }

    [Theory]
    [MemberData(nameof(EmptyElementFiles))]
    public void GivenEmptyElement_WhenCheckIsEmpty_ThenResultShouldBeTrue(string file)
    {
        // Arrange
        var json = File.ReadAllText(Path.Join("TestResources", file));
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);

        // Act & Assert
        EmptyElement.IsEmptyElement(element).ShouldBeTrue();
        EmptyElement.IsEmpty(element).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(EmptyElementFiles))]
    public void GivenEmptyElementJson_WhenCheckIsEmpty_ThenResultShouldBeTrue(string file)
    {
        // Arrange
        var json = File.ReadAllText(Path.Join("TestResources", file));

        // Act & Assert
        EmptyElement.IsEmptyElement(json).ShouldBeTrue();
        EmptyElement.IsEmpty(json).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(NonEmptyElementFiles))]
    public void GivenNonEmptyElementJson_WhenCheckIsEmpty_ThenResultShouldBeFalse(string file)
    {
        // Arrange
        var json = File.ReadAllText(Path.Join("TestResources", file));

        // Act & Assert
        EmptyElement.IsEmptyElement(json).ShouldBeFalse();
        EmptyElement.IsEmpty(json).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(NonEmptyElementFiles))]
    public void GivenNonEmptyElement_WhenCheckIsEmpty_ThenResultShouldBeFalse(string file)
    {
        // Arrange
        var json = File.ReadAllText(Path.Join("TestResources", file));
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);

        // Act & Assert
        EmptyElement.IsEmptyElement(element).ShouldBeFalse();
        EmptyElement.IsEmpty(element).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(NonEmptyElementContent))]
    public void GivenNonEmptyContent_WhenCheckIsEmpty_ThenResultShouldBeFalse(string content)
    {
        // Act & Assert
        EmptyElement.IsEmptyElement(content).ShouldBeFalse();
        EmptyElement.IsEmpty(content).ShouldBeFalse();
    }
}
