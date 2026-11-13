// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Api.Middleware;
using Ignixa.Application.Features.SemanticSearch;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Middleware;

/// <summary>
/// Task 4 fix round 1 (semantic-vector-search slice 1): <see cref="EmbeddingProviderContractException"/>
/// and <see cref="SemanticSearchDefinitionException"/> are both server faults, not request errors --
/// before this fix, <see cref="SemanticIndexer"/> threw bare <see cref="InvalidOperationException"/> for
/// provider count/dimension mismatches, which <see cref="FhirExceptionMiddleware"/>'s generic fallback
/// maps to 400. Subclassing <see cref="Ignixa.Serialization.Abstractions.FhirException"/> needs no
/// dedicated mapping branch -- the middleware maps any <c>FhirException</c> by its own
/// <see cref="Ignixa.Serialization.Abstractions.FhirException.StatusCode"/> -- but this pins the
/// resulting HTTP response so a regression in either exception's <c>StatusCode</c> fails a test here.
/// </summary>
public class EmbeddingProviderContractExceptionMappingTests
{
    [Fact]
    public async Task GivenEmbeddingProviderContractException_WhenUsingExceptionMiddleware_ThenReturns500WithOperationOutcome()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new FhirExceptionMiddleware(
            _ => throw new EmbeddingProviderContractException("The embedding provider returned 1 embeddings for 2 passages."),
            NullLogger<FhirExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(500);
        context.Response.ContentType.ShouldBe("application/fhir+json");
        context.Response.Body.Position = 0;
        var outcome = JsonNode.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync())!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        var issue = outcome["issue"]!.AsArray().ShouldHaveSingleItem()!;
        issue["severity"]!.GetValue<string>().ShouldBe("error");
        issue["code"]!.GetValue<string>().ShouldBe("exception");
    }

    [Fact]
    public async Task GivenSemanticSearchDefinitionException_WhenUsingExceptionMiddleware_ThenReturns500WithOperationOutcome()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new FhirExceptionMiddleware(
            _ => throw new SemanticSearchDefinitionException("Semantic search parameter 'http://example.org/semantic-text' extracted a non-string value."),
            NullLogger<FhirExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(500);
        context.Response.ContentType.ShouldBe("application/fhir+json");
        context.Response.Body.Position = 0;
        var outcome = JsonNode.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync())!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        var issue = outcome["issue"]!.AsArray().ShouldHaveSingleItem()!;
        issue["severity"]!.GetValue<string>().ShouldBe("error");
        issue["code"]!.GetValue<string>().ShouldBe("exception");
    }
}
