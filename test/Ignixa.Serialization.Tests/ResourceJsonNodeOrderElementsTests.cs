// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Json;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.Tests.TestData;
using Ignixa.Specification.Generated;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class ResourceJsonNodeOrderElementsTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static TheoryData<string> ResourceTypes() => ElementOrderingSamples.ResourceTypes();

    [Theory]
    [MemberData(nameof(ResourceTypes))]
    public void GivenFirelyResourceWithReversedProperties_WhenOrderElements_ThenMatchesFirelyOrder(string resourceType)
    {
        string firelyJson = ElementOrderingSamples.FirelyJson(resourceType);
        ResourceJsonNode resource = ResourceJsonNode.Parse(ElementOrderingSamples.ReverseAllProperties(firelyJson));

        resource.OrderElements(_schema);

        Serialize(resource).ShouldBe(ElementOrderingSamples.Canonical(firelyJson));
    }

    [Fact]
    public void GivenPrimitiveWithExtensionSibling_WhenOrderElements_ThenSiblingFollowsPrimitive()
    {
        ResourceJsonNode resource = ResourceJsonNode.Parse(
            """{"_birthDate":{"extension":[{"url":"http://e","valueString":"x"}]},"gender":"male","birthDate":"1970-01-01","resourceType":"Patient"}""");

        resource.OrderElements(_schema);

        Serialize(resource).ShouldBe("""{"resourceType":"Patient","gender":"male","birthDate":"1970-01-01","_birthDate":{"extension":[{"url":"http://e","valueString":"x"}]}}""");
    }

    [Fact]
    public void GivenUnknownProperties_WhenOrderElements_ThenTheyFollowKnownElementsInSourceOrder()
    {
        ResourceJsonNode resource = ResourceJsonNode.Parse("""{"zeta":1,"id":"a","alpha":2,"resourceType":"Patient"}""");

        resource.OrderElements(_schema);

        Serialize(resource).ShouldBe("""{"resourceType":"Patient","id":"a","zeta":1,"alpha":2}""");
    }

    [Fact]
    public void GivenMetaAccessedBeforeOrdering_WhenOrderElementsThenMetaChanged_ThenChangeIsSerialized()
    {
        ResourceJsonNode resource = ResourceJsonNode.Parse("""{"meta":{"versionId":"1"},"id":"a","resourceType":"Patient"}""");
        var meta = resource.Meta;

        resource.OrderElements(_schema);
        meta.VersionId = "2";

        Serialize(resource).ShouldBe("""{"resourceType":"Patient","id":"a","meta":{"versionId":"2"}}""");
    }

    [Fact]
    public void GivenElementChildrenReadBeforeOrdering_WhenOrderElements_ThenElementReflectsNewOrder()
    {
        ResourceJsonNode resource = ResourceJsonNode.Parse("""{"gender":"male","id":"a","resourceType":"Patient"}""");
        _ = resource.ToElement(_schema).Children().ToList();

        resource.OrderElements(_schema);

        resource.ToElement(_schema).Children().Select(child => child.Name).ShouldBe(["id", "gender"]);
    }

    private static string Serialize(ResourceJsonNode resource) =>
        JsonSerializer.Serialize(resource.MutableNode, ElementOrderingSamples.Relaxed);
}
