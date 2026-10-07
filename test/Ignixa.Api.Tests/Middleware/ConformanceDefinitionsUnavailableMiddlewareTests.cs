using Ignixa.Api.Middleware;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.Middleware;

public class ConformanceDefinitionsUnavailableMiddlewareTests
{
    [Fact]
    public async Task GivenDefinitionsRemainStale_WhenMiddlewareHandlesTheFailure_ThenItReturns503WithRetryAfter()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new FhirExceptionMiddleware(
            _ => throw new ConformanceDefinitionsUnavailableException(
                TimeSpan.FromSeconds(17),
                new StaleConformanceDefinitionsException(101, 11, 29)),
            NullLogger<FhirExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().ShouldBe("17");
        context.Response.Body.Length.ShouldBeGreaterThan(0);
    }
}
