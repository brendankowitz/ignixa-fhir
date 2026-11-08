// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Api.Middleware;
using Ignixa.Validation.Abstractions;
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

    // Regression: at 96d6150 the in-memory terminology service's unsupported $subsumes became a 500 exception; at 4073a28
    // it was a plain NotSupportedException, so this does not compile there (no dedicated type).
    [Fact]
    public async Task GivenAnOperationTheTerminologyServiceDoesNotSupport_WhenHandled_ThenRespondsNotImplementedWithNotSupportedOutcome()
    {
        // Arrange
        var (context, body) = CreateContext();
        var middleware = new FhirExceptionMiddleware(
            _ => throw new TerminologyOperationNotSupportedException("$subsumes is not supported by the in-memory terminology service."),
            NullLogger<FhirExceptionMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.ShouldBe(StatusCodes.Status501NotImplemented);
        IssueCode(body).ShouldBe("not-supported");
    }

    // Regression: at 4073a28 every NotSupportedException became 501, so a fault inside the server (a stream that cannot
    // seek) read as an unsupported operation.
    [Fact]
    public async Task GivenANotSupportedExceptionFromTheRuntime_WhenHandled_ThenRespondsInternalServerErrorWithExceptionOutcome()
    {
        // Arrange
        var (context, body) = CreateContext();
        var middleware = new FhirExceptionMiddleware(
            _ => throw new NotSupportedException("Specified method is not supported."),
            NullLogger<FhirExceptionMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        IssueCode(body).ShouldBe("exception");
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
