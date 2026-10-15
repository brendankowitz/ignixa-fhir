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

    [Theory]
    [MemberData(nameof(ResourceTypes))]
    public void GivenFirelyOrderedResource_WhenOrderElements_ThenResourceAndCachesAreUnchanged(string resourceType)
    {
        string firelyJson = ElementOrderingSamples.FirelyJson(resourceType);
        ResourceJsonNode resource = ResourceJsonNode.Parse(firelyJson);
        var element = resource.ToElement(_schema);

        resource.OrderElements(_schema);

        Serialize(resource).ShouldBe(ElementOrderingSamples.Canonical(firelyJson));
        resource.ToElement(_schema).ShouldBeSameAs(element);
    }

    [Fact]
    public void GivenOnlyNestedObjectOutOfOrder_WhenOrderElements_ThenElementReflectsNewOrder()
    {
        ResourceJsonNode resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"a","name":[{"given":["x"],"family":"f"}]}""");
        _ = resource.ToElement(_schema).Children("name").Single().Children().ToList();

        resource.OrderElements(_schema);

        resource.ToElement(_schema).Children("name").Single().Children().Select(child => child.Name).ShouldBe(["family", "given"]);
    }

    [Fact]
    public void GivenPrimitiveArraySiblingWithNulls_WhenOrderElements_ThenNullsKeptAndSiblingObjectsOrdered()
    {
        ResourceJsonNode resource = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","name":[{"_given":[null,{"extension":[{"valueString":"x","url":"http://e"}],"id":"g2"}],"given":["a","b"]}]}""");

        resource.OrderElements(_schema);

        Serialize(resource).ShouldBe(
            """{"resourceType":"Patient","name":[{"given":["a","b"],"_given":[null,{"id":"g2","extension":[{"url":"http://e","valueString":"x"}]}]}]}""");
    }

    [Fact]
    public void GivenChoiceNameWithUndeclaredType_WhenOrderElements_ThenItIsTreatedAsUnknown()
    {
        ResourceJsonNode resource = ResourceJsonNode.Parse(
            """{"valueZz":1,"_deceasedBoolean":{"id":"d"},"deceasedBoolean":false,"resourceType":"Patient","id":"a"}""");

        resource.OrderElements(_schema);

        Serialize(resource).ShouldBe("""{"resourceType":"Patient","id":"a","deceasedBoolean":false,"_deceasedBoolean":{"id":"d"},"valueZz":1}""");
    }

    [Fact]
    public void GivenElementNamedResourceTypeOutsideResourcePosition_WhenOrderElements_ThenItIsOrderedAsAnElement()
    {
        ResourceJsonNode resource = ResourceJsonNode.Parse(
            """{"instance":[{"name":"n","resourceType":"Patient","resourceId":"r1"}],"status":"draft","resourceType":"ExampleScenario"}""");

        resource.OrderElements(_schema);

        Serialize(resource).ShouldBe(
            """{"resourceType":"ExampleScenario","status":"draft","instance":[{"resourceId":"r1","resourceType":"Patient","name":"n"}]}""");
    }

    public static TheoryData<string> OtherVersionSchemas() => new() { "STU3", "R4B", "R5" };

    [Theory]
    [MemberData(nameof(OtherVersionSchemas))]
    public void GivenOtherFhirVersionSchema_WhenOrderElements_ThenBackbonesAndRecursiveItemsAreOrdered(string version)
    {
        Ignixa.Abstractions.ISchema schema = version switch
        {
            "STU3" => new STU3CoreSchemaProvider(),
            "R4B" => new R4BCoreSchemaProvider(),
            _ => new R5CoreSchemaProvider(),
        };
        ResourceJsonNode resource = ResourceJsonNode.Parse(
            """{"item":[{"item":[{"type":"string","linkId":"1.1"}],"type":"group","linkId":"1"}],"status":"active","resourceType":"Questionnaire"}""");

        resource.OrderElements(schema);

        Serialize(resource).ShouldBe(
            """{"resourceType":"Questionnaire","status":"active","item":[{"linkId":"1","type":"group","item":[{"linkId":"1.1","type":"string"}]}]}""");
    }

    private static string Serialize(ResourceJsonNode resource) =>
        JsonSerializer.Serialize(resource.MutableNode, ElementOrderingSamples.Relaxed);
}
