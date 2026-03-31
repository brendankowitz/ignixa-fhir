// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FieldResolverTests
{
    [Theory]
    [InlineData("Patient/123", "Patient", "123")]
    [InlineData("Observation/obs-1", "Observation", "obs-1")]
    public void GivenRelativeReference_WhenParsing_ThenReturnsResourceKey(
        string reference, string expectedType, string expectedId)
    {
        var result = FieldResolver.ParseFhirReference(reference);

        result.ShouldNotBeNull();
        result!.ResourceType.ShouldBe(expectedType);
        result.ResourceId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData("https://example.com/fhir/Patient/456", "Patient", "456")]
    [InlineData("http://server.org/base/Observation/obs-2", "Observation", "obs-2")]
    public void GivenAbsoluteReference_WhenParsing_ThenReturnsResourceKey(
        string reference, string expectedType, string expectedId)
    {
        var result = FieldResolver.ParseFhirReference(reference);

        result.ShouldNotBeNull();
        result!.ResourceType.ShouldBe(expectedType);
        result.ResourceId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData("Patient/123/_history/2", "Patient", "123")]
    [InlineData("https://server.com/fhir/Observation/obs-1/_history/5", "Observation", "obs-1")]
    public void GivenVersionedReference_WhenParsing_ThenReturnsResourceKeyWithoutVersion(
        string reference, string expectedType, string expectedId)
    {
        var result = FieldResolver.ParseFhirReference(reference);

        result.ShouldNotBeNull();
        result!.ResourceType.ShouldBe(expectedType);
        result.ResourceId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#contained-ref")]
    [InlineData("urn:uuid:some-guid")]
    [InlineData("just-a-string")]
    public void GivenUnresolvableReference_WhenParsing_ThenReturnsNull(string? reference)
    {
        var result = FieldResolver.ParseFhirReference(reference);

        result.ShouldBeNull();
    }

    [Fact]
    public void GivenJsonWithUnderscoreField_WhenAccessingPrimitiveExtension_ThenReturnsElement()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"birthDate":"1990-01-01","_birthDate":{"extension":[{"url":"http://example.com","valueString":"test"}]}}""");

        json.TryGetProperty("_birthDate", out var element).ShouldBeTrue();
        element.TryGetProperty("extension", out var extensions).ShouldBeTrue();
        extensions.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public void GivenArrayField_WhenApplyingOffsetAndCount_ThenReturnsSubset()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"name":[{"text":"A"},{"text":"B"},{"text":"C"},{"text":"D"}]}""");

        var items = json.GetProperty("name").EnumerateArray().ToList();
        var result = items.Skip(1).Take(2).ToList();

        result.Count.ShouldBe(2);
        result[0].GetProperty("text").GetString().ShouldBe("B");
        result[1].GetProperty("text").GetString().ShouldBe("C");
    }
}
