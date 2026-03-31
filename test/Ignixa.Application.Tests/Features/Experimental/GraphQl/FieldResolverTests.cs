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

    [Fact]
    public void GivenExistsExpression_WhenFiltering_ThenReturnsMatchingElements()
    {
        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"family":"Smith","given":["John"]}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"given":["Jane"]}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"family":"Doe","given":["Jim"]}"""),
        };

        var result = FieldResolver.ApplyFhirPathFilter(items, "family.exists()").ToList();

        result.Count.ShouldBe(2);
        result[0].GetProperty("family").GetString().ShouldBe("Smith");
        result[1].GetProperty("family").GetString().ShouldBe("Doe");
    }

    [Fact]
    public void GivenIndexExpression_WhenFiltering_ThenReturnsSingleElement()
    {
        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"text":"First"}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"text":"Second"}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"text":"Third"}"""),
        };

        var result = FieldResolver.ApplyFhirPathFilter(items, "$index = 1").ToList();

        result.Count.ShouldBe(1);
        result[0].GetProperty("text").GetString().ShouldBe("Second");
    }

    [Fact]
    public void GivenUnsupportedExpression_WhenFiltering_ThenReturnsAllElements()
    {
        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"a":1}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"a":2}"""),
        };

        var result = FieldResolver.ApplyFhirPathFilter(items, "complex.where(x > 1)").ToList();

        result.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenEqualityExpression_WhenFiltering_ThenReturnsMatchingElements()
    {
        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"use":"official","family":"Smith"}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"use":"temp","family":"Jones"}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"use":"official","family":"Doe"}"""),
        };

        var result = FieldResolver.ApplyFhirPathFilter(items, "use = 'official'").ToList();

        result.Count.ShouldBe(2);
        result[0].GetProperty("family").GetString().ShouldBe("Smith");
        result[1].GetProperty("family").GetString().ShouldBe("Doe");
    }

    [Fact]
    public void GivenInequalityExpression_WhenFiltering_ThenExcludesMatchingElements()
    {
        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"use":"official","family":"Smith"}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"use":"temp","family":"Jones"}"""),
        };

        var result = FieldResolver.ApplyFhirPathFilter(items, "use != 'official'").ToList();

        result.Count.ShouldBe(1);
        result[0].GetProperty("family").GetString().ShouldBe("Jones");
    }

    [Fact]
    public void GivenIndexOutOfRange_WhenFiltering_ThenReturnsEmpty()
    {
        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"text":"Only"}"""),
        };

        var result = FieldResolver.ApplyFhirPathFilter(items, "$index = 5").ToList();

        result.Count.ShouldBe(0);
    }
}
