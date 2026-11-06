// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Filters;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Filters;

public class FhirAuditFilterTests
{
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
    private readonly FhirAuditFilter _filter;
    private HttpRequestAuditEvent? _captured;

    public FhirAuditFilterTests()
    {
        _auditLogger.LogHttpRequest(Arg.Do<HttpRequestAuditEvent>(auditEvent => _captured = auditEvent));
        _filter = new FhirAuditFilter(_auditLogger, NullLogger<FhirAuditFilter>.Instance);
    }

    [Fact]
    public async Task GivenCustomAuditHeaders_WhenRequestSucceeds_ThenAuditEventCarriesThem()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.Headers["X-IGNIXA-AUDIT-OPERATIONID"] = "op-1";
        httpContext.Request.Headers["X-IGNIXA-AUDIT-BUNDLEID"] = "bundle-1";
        httpContext.Request.Headers["Authorization"] = "Bearer secret";
        var (context, next) = CreateFilterContext(httpContext);
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>()).Returns(ValueTask.FromResult<object?>(Results.Ok()));

        await _filter.InvokeAsync(context, next);

        _captured.ShouldNotBeNull();
        _captured.CustomHeaders.Count.ShouldBe(2);
        _captured.CustomHeaders["X-IGNIXA-AUDIT-OPERATIONID"].ShouldBe("op-1");
        _captured.CustomHeaders["X-IGNIXA-AUDIT-BUNDLEID"].ShouldBe("bundle-1");
    }

    [Fact]
    public async Task GivenNoCustomAuditHeaders_WhenRequestSucceeds_ThenAuditEventHasEmptyHeaders()
    {
        var (context, next) = CreateFilterContext(CreateHttpContext());
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>()).Returns(ValueTask.FromResult<object?>(Results.Ok()));

        await _filter.InvokeAsync(context, next);

        _captured.ShouldNotBeNull();
        _captured.CustomHeaders.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenTooManyCustomAuditHeaders_WhenInvoked_ThenHandlerIsSkippedAndRejectionIsAuditedWithoutHeaders()
    {
        var httpContext = CreateHttpContext();
        for (var i = 0; i < 11; i++)
        {
            httpContext.Request.Headers[$"X-IGNIXA-AUDIT-H{i}"] = "v";
        }

        var (context, next) = CreateFilterContext(httpContext);

        var exception = await Should.ThrowAsync<AuditHeaderCountExceededException>(
            () => _filter.InvokeAsync(context, next).AsTask());

        exception.StatusCode.ShouldBe(431);
        await next.DidNotReceive().Invoke(Arg.Any<EndpointFilterInvocationContext>());
        _captured.ShouldNotBeNull();
        _captured.StatusCode.ShouldBe(431);
        _captured.Outcome.ShouldBe("4");
        _captured.CustomHeaders.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenOversizedCustomAuditHeader_WhenInvoked_ThenHandlerIsSkippedWith431()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.Headers["X-IGNIXA-AUDIT-SITE"] = new string('x', 2049);
        var (context, next) = CreateFilterContext(httpContext);

        await Should.ThrowAsync<AuditHeaderTooLargeException>(() => _filter.InvokeAsync(context, next).AsTask());

        await next.DidNotReceive().Invoke(Arg.Any<EndpointFilterInvocationContext>());
        _captured.ShouldNotBeNull();
        _captured.StatusCode.ShouldBe(431);
    }

    [Fact]
    public async Task GivenHandlerThrowsFhirException_WhenInvoked_ThenAuditUsesItsStatusAndMinorFailureOutcome()
    {
        var (context, next) = CreateFilterContext(CreateHttpContext());
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns<ValueTask<object?>>(_ => throw new PreconditionFailedException("version mismatch"));

        await Should.ThrowAsync<PreconditionFailedException>(() => _filter.InvokeAsync(context, next).AsTask());

        _captured.ShouldNotBeNull();
        _captured.StatusCode.ShouldBe(412);
        _captured.Outcome.ShouldBe("4");
    }

    [Fact]
    public async Task GivenHandlerThrowsUnexpectedException_WhenInvoked_ThenAuditOutcomeIsSeriousFailure()
    {
        var (context, next) = CreateFilterContext(CreateHttpContext());
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns<ValueTask<object?>>(_ => throw new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => _filter.InvokeAsync(context, next).AsTask());

        _captured.ShouldNotBeNull();
        _captured.Outcome.ShouldBe("8");
    }

    [Theory]
    [InlineData(404, "4")]
    [InlineData(202, "0")]
    [InlineData(409, "4")]
    public async Task GivenHandlerReturnsStatusCodeResult_WhenInvoked_ThenAuditUsesTheResultStatus(int status, string outcome)
    {
        var (context, next) = CreateFilterContext(CreateHttpContext());
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>()).Returns(ValueTask.FromResult<object?>(Results.StatusCode(status)));

        await _filter.InvokeAsync(context, next);

        _captured.ShouldNotBeNull();
        _captured.StatusCode.ShouldBe(status);
        _captured.Outcome.ShouldBe(outcome);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/tenant/1/Patient/123";
        return httpContext;
    }

    private static (EndpointFilterInvocationContext Context, EndpointFilterDelegate Next) CreateFilterContext(HttpContext httpContext)
    {
        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        return (context, Substitute.For<EndpointFilterDelegate>());
    }
}
