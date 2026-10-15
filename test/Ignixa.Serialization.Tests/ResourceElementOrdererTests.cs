// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text;
using System.Text.Json;
using Ignixa.Serialization.Extensions;
using Ignixa.Serialization.Tests.TestData;
using Ignixa.Specification.Generated;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class ResourceElementOrdererTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static TheoryData<string> ResourceTypes() => ElementOrderingSamples.ResourceTypes();

    [Theory]
    [MemberData(nameof(ResourceTypes))]
    public void GivenFirelyResourceWithReversedProperties_WhenOrdered_ThenMatchesFirelyOrder(string resourceType)
    {
        string firelyJson = ElementOrderingSamples.FirelyJson(resourceType);
        string scrambled = ElementOrderingSamples.ReverseAllProperties(firelyJson);

        string ordered = Order(scrambled);

        ElementOrderingSamples.Canonical(ordered).ShouldBe(ElementOrderingSamples.Canonical(firelyJson));
    }

    [Theory]
    [MemberData(nameof(ResourceTypes))]
    public void GivenFirelyOrderedResource_WhenOrdered_ThenReturnsInputUnchanged(string resourceType)
    {
        ReadOnlyMemory<byte> input = Encoding.UTF8.GetBytes(ElementOrderingSamples.FirelyJson(resourceType));

        ReadOnlyMemory<byte> ordered = ResourceElementOrderer.Order(input, _schema);

        ordered.Equals(input).ShouldBeTrue();
    }

    [Fact]
    public void GivenPrimitiveWithExtensionSibling_WhenOrdered_ThenSiblingFollowsPrimitive()
    {
        string json = """{"_birthDate":{"extension":[{"url":"http://e","valueString":"x"}]},"gender":"male","birthDate":"1970-01-01","resourceType":"Patient"}""";

        string ordered = Order(json);

        ordered.ShouldBe("""{"resourceType":"Patient","gender":"male","birthDate":"1970-01-01","_birthDate":{"extension":[{"url":"http://e","valueString":"x"}]}}""");
    }

    [Fact]
    public void GivenUnknownProperties_WhenOrdered_ThenTheyFollowKnownElementsInSourceOrder()
    {
        string json = """{"zeta":1,"id":"a","alpha":2,"resourceType":"Patient"}""";

        string ordered = Order(json);

        ordered.ShouldBe("""{"resourceType":"Patient","id":"a","zeta":1,"alpha":2}""");
    }

    [Fact]
    public void GivenDecimalAndEscapedString_WhenOrdered_ThenLeafBytesAreCopiedVerbatim()
    {
        string json = """{"valueQuantity":{"unit":"mg/dl","value":1.50},"status":"final","resourceType":"Observation"}""";

        string ordered = Order(json);

        ordered.ShouldBe("""{"resourceType":"Observation","status":"final","valueQuantity":{"value":1.50,"unit":"mg/dl"}}""");
    }

    [Fact]
    public void GivenArray_WhenOrdered_ThenElementOrderIsPreserved()
    {
        string json = """{"name":[{"given":["b","a"],"family":"Z"},{"family":"Y"}],"resourceType":"Patient"}""";

        string ordered = Order(json);

        ordered.ShouldBe("""{"resourceType":"Patient","name":[{"family":"Z","given":["b","a"]},{"family":"Y"}]}""");
    }

    [Fact]
    public void GivenNonObjectJson_WhenOrdered_ThenThrowsJsonException()
    {
        Should.Throw<JsonException>(() => ResourceElementOrderer.Order(Encoding.UTF8.GetBytes("[1]"), _schema));
    }

    private string Order(string json) =>
        Encoding.UTF8.GetString(ResourceElementOrderer.Order(Encoding.UTF8.GetBytes(json), _schema).Span);
}
