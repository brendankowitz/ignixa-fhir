// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Endpoints;
using Ignixa.Serialization;
using Ignixa.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.Endpoints;

public class TransactionResponseWriteTests
{
    [Fact]
    public async Task GivenAWriteFailureAfterTheResponseStarted_WhenWritingTheTransactionResponse_ThenTheConnectionIsAbortedAndTheFailureEscapes()
    {
        // Arrange: a response over the flush threshold whose body stream fails on its second write. Once the
        // first chunk is out the status can no longer change, so a clean completion would hand the client a
        // complete-looking, truncated 200.
        var (context, lifetime) = CreateContext(new FailingStream(failOnWrite: 2));

        // Act
        var thrown = await Should.ThrowAsync<IOException>(
            () => FhirEndpoints.WriteTransactionResponseAsync(context, LargeBundle(), NullLogger.Instance, CancellationToken.None));

        // Assert
        thrown.ShouldNotBeNull();
        lifetime.Aborted.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAFailureBeforeTheResponseStarted_WhenWritingTheTransactionResponse_ThenTheConnectionIsLeftForTheErrorHandler()
    {
        // Arrange: nothing has been sent, so FhirExceptionMiddleware can still write a status-coded error.
        var (context, lifetime) = CreateContext(new FailingStream(failOnWrite: 1));

        // Act
        await Should.ThrowAsync<IOException>(
            () => FhirEndpoints.WriteTransactionResponseAsync(context, LargeBundle(), NullLogger.Instance, CancellationToken.None));

        // Assert
        lifetime.Aborted.ShouldBeFalse();
    }

    private static Bundle LargeBundle()
    {
        var entries = string.Join(",", Enumerable.Range(0, 100).Select(i =>
            "{\"response\":{\"status\":\"201 Created\"},\"resource\":{\"resourceType\":\"Basic\",\"id\":\"b" + i +
            "\",\"text\":{\"status\":\"generated\",\"div\":\"<div>" + new string('x', 10_000) + "</div>\"}}}"));
        return JsonSourceNodeFactory.Parse<Bundle>(
            "{\"resourceType\":\"Bundle\",\"type\":\"transaction-response\",\"entry\":[" + entries + "]}");
    }

    private static (DefaultHttpContext Context, RecordingLifetimeFeature Lifetime) CreateContext(FailingStream body)
    {
        var context = new DefaultHttpContext();
        var lifetime = new RecordingLifetimeFeature();
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        context.Features.Set<IHttpResponseFeature>(new StartedOnWriteResponseFeature(body));
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));
        return (context, lifetime);
    }

    private sealed class FailingStream(int failOnWrite) : MemoryStream
    {
        public int Writes { get; private set; }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++Writes >= failOnWrite)
            {
                throw new IOException("The client connection was reset.");
            }

            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class StartedOnWriteResponseFeature(FailingStream body) : HttpResponseFeature
    {
        public override bool HasStarted => body.Writes > 0 && body.Length > 0;
    }

    private sealed class RecordingLifetimeFeature : IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }

        public CancellationToken RequestAborted { get; set; }

        public void Abort() => Aborted = true;
    }
}
