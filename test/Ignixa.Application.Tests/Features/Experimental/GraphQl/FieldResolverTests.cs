// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Ignixa.Specification.Generated;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FieldResolverTests
{
    private static readonly IFhirSchemaProvider SchemaProvider = new R4CoreSchemaProvider();

    [Fact]
    public void GivenPrimitiveExtensionElement_WhenFilteringExtensionsByUrl_ThenReturnsMatchingExtension()
    {
        var element = JsonSerializer.Deserialize<JsonElement>(
            """{"extension":[{"url":"http://example.com/a","valueString":"a"},{"url":"http://example.com/b","valueString":"b"}]}""");

        var result = FieldResolver.FilterExtensionsByUrl(element, "http://example.com/b").ToList();

        result.Count.ShouldBe(1);
        FieldResolver.GetStringProperty(result[0], "valueString").ShouldBe("b");
    }

    [Fact]
    public void GivenArrayField_WhenSelectingByIndexShorthand_ThenReturnsSingleElement()
    {
        var items = JsonSerializer.Deserialize<JsonElement>(
            """[{"text":"A"},{"text":"B"},{"text":"C"},{"text":"D"}]""")
            .EnumerateArray().ToList();

        var result = FieldResolver.ApplyFhirPathFilter(items, "$index = 1", SchemaProvider, null).ToList();

        result.Count.ShouldBe(1);
        FieldResolver.GetStringProperty(result[0], "text").ShouldBe("B");
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

        var result = FieldResolver.ApplyFhirPathFilter(items, "family.exists()", SchemaProvider, "HumanName").ToList();

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

        var result = FieldResolver.ApplyFhirPathFilter(items, "$index = 1", SchemaProvider, null).ToList();

        result.Count.ShouldBe(1);
        result[0].GetProperty("text").GetString().ShouldBe("Second");
    }

    [Fact]
    public void GivenInvalidExpression_WhenFiltering_ThenThrowsGraphQLExceptionWithFhirPathInvalidCode()
    {
        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"a":1}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"a":2}"""),
        };

        var ex = Should.Throw<GraphQLException>(() =>
            FieldResolver.ApplyFhirPathFilter(items, "(invalid", SchemaProvider, null).ToList());

        ex.Errors[0].Code.ShouldBe("FHIRPATH_INVALID");
    }

    [Fact]
    public void GivenInvalidExpression_WhenFiltering_ThenDoesNotFailOpen()
    {
        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"status":"active"}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"status":"inactive"}"""),
        };

        Should.Throw<GraphQLException>(() =>
            FieldResolver.ApplyFhirPathFilter(items, "status = 'active'))", SchemaProvider, "Medication").ToList());
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

        var result = FieldResolver.ApplyFhirPathFilter(items, "use = 'official'", SchemaProvider, "HumanName").ToList();

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

        var result = FieldResolver.ApplyFhirPathFilter(items, "use != 'official'", SchemaProvider, "HumanName").ToList();

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

        var result = FieldResolver.ApplyFhirPathFilter(items, "$index = 5", SchemaProvider, null).ToList();

        result.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenExpressionThatThrowsAtEvaluation_WhenFiltering_ThenThrowsGraphQLExceptionWithFhirPathInvalidCode()
    {
        // Arrange — the expression parses cleanly, but the schema provider throws while the
        // evaluator resolves type metadata, simulating a FHIRPath evaluation-time failure.
        var throwingSchema = Substitute.For<IFhirSchemaProvider>();
        throwingSchema.GetTypeDefinition(Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("schema lookup blew up"));

        var items = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("""{"family":"Smith"}"""),
        };

        // Act & Assert — enumeration triggers the per-item evaluation
        var ex = Should.Throw<GraphQLException>(() =>
            FieldResolver.ApplyFhirPathFilter(items, "family.exists()", throwingSchema, "HumanName").ToList());

        ex.Errors[0].Code.ShouldBe("FHIRPATH_INVALID");
    }
}
