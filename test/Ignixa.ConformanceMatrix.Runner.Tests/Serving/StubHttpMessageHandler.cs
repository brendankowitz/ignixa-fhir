namespace Ignixa.ConformanceMatrix.Runner.Tests.Serving;

/// <summary>An <see cref="HttpMessageHandler"/> whose response is produced by a delegate — no real network.</summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(responder(request));
}
