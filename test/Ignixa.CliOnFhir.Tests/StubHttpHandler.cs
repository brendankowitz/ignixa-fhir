// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>An HTTP handler that answers from a function and records every request; tests never reach the network.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<Uri> _requests = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<Uri> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public static HttpResponseMessage Bytes(byte[] content, string mediaType = "application/octet-stream") => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(content) { Headers = { ContentType = new(mediaType) } },
    };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new ByteArrayContent([]) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requests.Add(request.RequestUri!);
        }

        return Task.FromResult(respond(request));
    }
}
