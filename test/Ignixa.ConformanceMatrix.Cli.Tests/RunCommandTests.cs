using System.Net;
using System.Text;
using Shouldly;
using Ignixa.ConformanceMatrix.Cli.Commands;

namespace Ignixa.ConformanceMatrix.Cli.Tests;

public class RunCommandTests
{
    [Fact]
    public async Task GivenSuccessfulMetadataResponse_WhenFetching_ThenReturnsParsedCapabilityStatement()
    {
        using var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"resourceType":"CapabilityStatement","status":"active"}""",
                    Encoding.UTF8,
                    "application/fhir+json")
            });
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("CapabilityStatement");
    }

    [Fact]
    public async Task GivenNonSuccessMetadataResponse_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenUnparseableMetadataBody_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json", Encoding.UTF8, "application/fhir+json")
            });
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenNetworkFailure_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenCancellationRequested_WhenFetching_ThenThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHandler(_ => throw new OperationCanceledException());
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        await Should.ThrowAsync<OperationCanceledException>(
            () => RunCommand.FetchCapabilityStatementAsync(httpClient, cts.Token));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
