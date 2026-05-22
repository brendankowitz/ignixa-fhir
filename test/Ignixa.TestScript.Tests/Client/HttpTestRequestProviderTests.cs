using System.Net;
using System.Text;
using Ignixa.Serialization;
using Ignixa.TestScript.Client;

namespace Ignixa.TestScript.Tests.Client;

public class HttpTestRequestProviderTests
{
    [Fact]
    public async Task GivenJsonResponse_WhenSending_ThenBodyIsParsed()
    {
        using var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"resourceType":"Patient","id":"abc"}""",
                    Encoding.UTF8,
                    "application/fhir+json")
            });
        using var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://test/")
        };

        var provider = new HttpTestRequestProvider(httpClient);
        var response = await provider.ExecuteAsync(
            new TestRequest { Method = HttpMethod.Get, Url = "/Patient/abc" },
            CancellationToken.None);

        response.StatusCode.ShouldBe(200);
        response.Body.ShouldNotBeNull();
        response.Body.Id.ShouldBe("abc");
    }

    [Fact]
    public async Task GivenNonJsonResponse_WhenSending_ThenBodyIsNull()
    {
        using var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("plain text body", Encoding.UTF8, "text/plain")
            });
        using var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://test/")
        };

        var provider = new HttpTestRequestProvider(httpClient);
        var response = await provider.ExecuteAsync(
            new TestRequest { Method = HttpMethod.Get, Url = "/something" },
            CancellationToken.None);

        response.StatusCode.ShouldBe(200);
        response.Body.ShouldBeNull();
    }

    [Fact]
    public async Task GivenResponseWithHeaders_WhenSending_ThenHeadersAreMerged()
    {
        using var handler = new StubHandler(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/fhir+json")
            };
            msg.Headers.Add("Location", "http://test/Patient/created-1");
            return msg;
        });
        using var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://test/")
        };

        var provider = new HttpTestRequestProvider(httpClient);
        var response = await provider.ExecuteAsync(
            new TestRequest { Method = HttpMethod.Post, Url = "/Patient", Body = JsonSourceNodeFactory.Parse("{}") },
            CancellationToken.None);

        response.StatusCode.ShouldBe(201);
        response.Headers.ShouldContainKey("Location");
        response.Headers["Location"].ShouldBe("http://test/Patient/created-1");
        response.Headers.ShouldContainKey("Content-Type");
    }

    [Fact]
    public async Task GivenRequestWithBody_WhenSending_ThenContentTypeIsSet()
    {
        string? capturedContentType = null;
        using var handler = new StubHandler(req =>
        {
            capturedContentType = req.Content?.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/fhir+json")
            };
        });
        using var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://test/")
        };

        var provider = new HttpTestRequestProvider(httpClient);
        await provider.ExecuteAsync(
            new TestRequest
            {
                Method = HttpMethod.Post,
                Url = "/Patient",
                Body = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient"}""")
            },
            CancellationToken.None);

        capturedContentType.ShouldBe("application/fhir+json");
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
