// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.Middleware;

public class FhirExceptionMiddlewareTests
{
    [Fact]
    public async Task GivenKestrelRejectsAnOversizedBody_WhenHandled_ThenRespondsPayloadTooLargeWithTooCostlyOutcome()
    {
        // Arrange
        var (context, body) = CreateContext();
        var middleware = new FhirExceptionMiddleware(
            _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            NullLogger<FhirExceptionMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        IssueCode(body).ShouldBe("too-costly");
    }

    [Fact]
    public async Task GivenAMalformedRequestRejectedByTheServer_WhenHandled_ThenKeepsItsClientErrorStatus()
    {
        // Arrange
        var (context, body) = CreateContext();
        var middleware = new FhirExceptionMiddleware(
            _ => throw new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest),
            NullLogger<FhirExceptionMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        IssueCode(body).ShouldBe("invalid");
    }

    private static (DefaultHttpContext Context, MemoryStream Body) CreateContext()
    {
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (context, body);
    }

    private static string? IssueCode(MemoryStream body)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToArray());
        return document.RootElement.GetProperty("issue")[0].GetProperty("code").GetString();
    }
}
