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
/// Task 4 (semantic-vector-search slice 1): <see cref="EmbeddingUnavailableException"/> subclasses
/// <see cref="Ignixa.Serialization.Abstractions.FhirException"/>, so <see cref="FhirExceptionMiddleware"/>
/// needs no dedicated mapping branch -- it maps any <c>FhirException</c> by its own
/// <see cref="Ignixa.Serialization.Abstractions.FhirException.StatusCode"/>. This pins the resulting
/// HTTP response rather than the exception type alone, so a regression in that generic mapping (or in
/// <see cref="EmbeddingUnavailableException.StatusCode"/> itself) fails a test here.
/// </summary>
public class EmbeddingUnavailableExceptionMappingTests
{
    [Fact]
    public async Task GivenEmbeddingUnavailableException_WhenUsingExceptionMiddleware_ThenReturns503WithOperationOutcome()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new FhirExceptionMiddleware(
            _ => throw new EmbeddingUnavailableException("The embedding provider is unavailable.", new HttpRequestException("boom")),
            NullLogger<FhirExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(503);
        context.Response.ContentType.ShouldBe("application/fhir+json");
        context.Response.Body.Position = 0;
        var outcome = JsonNode.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync())!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        var issue = outcome["issue"]!.AsArray().ShouldHaveSingleItem()!;
        issue["severity"]!.GetValue<string>().ShouldBe("error");
        issue["code"]!.GetValue<string>().ShouldBe("transient");
    }
}
