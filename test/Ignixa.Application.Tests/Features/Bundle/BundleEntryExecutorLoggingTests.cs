// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Tests.TestDoubles;
using Ignixa.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.IO;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle;

/// <summary>
/// A bundle entry runs its endpoint without <c>FhirExceptionMiddleware</c>, so the entry executor logs the entry's
/// failures itself and must keep their messages out of the log as the middleware does: a FHIRPath Patch failure's
/// message quotes the request's path, and an evaluation failure's can quote resource values.
/// </summary>
public class BundleEntryExecutorLoggingTests
{
    private const string RequestText = "Patient.name.where(family = 'Secret-Family-Name')";

    [Fact]
    public async Task GivenAnEntryWhoseEndpointThrowsAFhirException_WhenExecuted_ThenItsMessageDoesNotReachTheLog() =>
        await AssertFailureIsLoggedWithoutItsMessage(new BadRequestException($"Path '{RequestText}' did not match any element"));

    [Fact]
    public async Task GivenAnEntryWhoseEndpointThrowsAnUnexpectedException_WhenExecuted_ThenItsMessageDoesNotReachTheLog() =>
        await AssertFailureIsLoggedWithoutItsMessage(new InvalidOperationException($"Path '{RequestText}' could not be evaluated"));

    private static async Task AssertFailureIsLoggedWithoutItsMessage(Exception failure)
    {
        // Arrange
        var logger = new CapturingLogger<BundleEntryExecutor>();
        var executor = CreateExecutor(failure, logger);

        // Act
        await executor.ExecuteAsync(PatchEntry(), new ReferenceResolutionContext(), CancellationToken.None);

        // Assert
        logger.Messages.ShouldContain(message => message.Contains(failure.GetType().FullName!, StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("Secret-Family-Name", StringComparison.Ordinal));
        logger.Exceptions.ShouldBeEmpty();
    }

    private static BundleEntryExecutor CreateExecutor(Exception failure, CapturingLogger<BundleEntryExecutor> logger)
    {
        var pipelineExecutor = Substitute.For<IPipelineExecutor>();
        pipelineExecutor.ExecuteAsync(Arg.Any<HttpContext>()).Returns(Task.FromException(failure));

        return new BundleEntryExecutor(
            pipelineExecutor,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new FhirRequestContextAccessor { RequestContext = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 1) },
            new RecyclableMemoryStreamManager(),
            logger);
    }

    private static BundleEntryContext PatchEntry() =>
        new()
        {
            Index = 0,
            HttpVerb = "PATCH",
            ResourceType = "Patient",
            ResourceId = "1",
            RequestUrl = "Patient/1",
            Resource = null,
            FullUrl = null
        };
}
