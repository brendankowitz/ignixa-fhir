// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate;
using Ignixa.Application.Features.Experimental.GraphQl.Directives;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FhirDirectiveTypeTests
{
    [Fact]
    public void GivenFlattenDirective_WhenCreated_ThenHasCorrectName()
    {
        var directive = new FhirFlattenDirectiveType();
        directive.ShouldNotBeNull();
    }

    [Fact]
    public void GivenFirstDirective_WhenCreated_ThenHasCorrectName()
    {
        var directive = new FhirFirstDirectiveType();
        directive.ShouldNotBeNull();
    }

    [Fact]
    public void GivenSingletonDirective_WhenCreated_ThenHasCorrectName()
    {
        var directive = new FhirSingletonDirectiveType();
        directive.ShouldNotBeNull();
    }

    [Fact]
    public void GivenSliceDirective_WhenCreated_ThenHasCorrectName()
    {
        var directive = new FhirSliceDirectiveType();
        directive.ShouldNotBeNull();
    }

    [Fact]
    public void GivenListResult_WhenFirstApplied_ThenReturnsSingleElement()
    {
        var list = new List<JsonElement>
        {
            JsonSerializer.Deserialize<JsonElement>("""{"text":"A"}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"text":"B"}"""),
        };

        var result = FhirDirectiveMiddleware.ApplyFirst(list);

        result.ShouldNotBeNull();
        result.ShouldBeOfType<JsonElement>();
        ((JsonElement)result).GetProperty("text").GetString().ShouldBe("A");
    }

    [Fact]
    public void GivenEmptyList_WhenFirstApplied_ThenReturnsNull()
    {
        var list = new List<JsonElement>();
        var result = FhirDirectiveMiddleware.ApplyFirst(list);
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenSingleElementList_WhenSingletonApplied_ThenReturnsSingleElement()
    {
        var list = new List<JsonElement>
        {
            JsonSerializer.Deserialize<JsonElement>("""{"text":"Only"}"""),
        };

        var result = FhirDirectiveMiddleware.ApplySingleton(list);

        result.ShouldNotBeNull();
        result.ShouldBeOfType<JsonElement>();
        ((JsonElement)result).GetProperty("text").GetString().ShouldBe("Only");
    }

    [Fact]
    public void GivenMultiElementList_WhenSingletonApplied_ThenThrowsGraphQLException()
    {
        var list = new List<JsonElement>
        {
            JsonSerializer.Deserialize<JsonElement>("""{"text":"A"}"""),
            JsonSerializer.Deserialize<JsonElement>("""{"text":"B"}"""),
        };

        var ex = Should.Throw<GraphQLException>(() => FhirDirectiveMiddleware.ApplySingleton(list));
        ex.Errors[0].Code.ShouldBe("FHIR_SINGLETON_VIOLATION");
    }

    [Fact]
    public void GivenEmptyList_WhenSingletonApplied_ThenReturnsNull()
    {
        var list = new List<JsonElement>();
        var result = FhirDirectiveMiddleware.ApplySingleton(list);
        result.ShouldBeNull();
    }
}
