using System.Net;
using System.Text;
using System.Text.Json;
using Ignixa.ConformanceMatrix.Cli.Serving;
using Shouldly;

namespace Ignixa.ConformanceMatrix.Cli.Tests.Serving;

public class ClientCredentialsTokenHandlerTests
{
    private static TokenClientConfiguration MakeConfig() =>
        new("http://token.test/connect/token", "client-id", "client-secret", "system/*.read");

    private static HttpResponseMessage TokenResponse(string accessToken, int expiresIn)
    {
        var body = JsonSerializer.Serialize(new { access_token = accessToken, expires_in = expiresIn, token_type = "Bearer" });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Uri FhirResource(string path) => new($"http://fhir.test/{path}");

    [Fact]
    public async Task GivenTwoRequests_WhenTokenIsStillValid_ThenTokenEndpointIsHitOnce()
    {
        // Arrange: expires_in far in the future so the second call reuses the cached token.
        var tokenCalls = 0;
        using var tokenHandler = new StubHttpMessageHandler(_ => { tokenCalls++; return TokenResponse("token-abc", 3600); });
        var innerCalls = 0;
        using var innerHandler = new StubHttpMessageHandler(_ => { innerCalls++; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var handler = new ClientCredentialsTokenHandler(MakeConfig(), tokenHandler) { InnerHandler = innerHandler };
        using var client = new HttpClient(handler);

        // Act
        await client.GetAsync(FhirResource("Patient/1"));
        await client.GetAsync(FhirResource("Patient/2"));

        // Assert
        tokenCalls.ShouldBe(1);
        innerCalls.ShouldBe(2);
    }

    [Fact]
    public async Task GivenRequest_WhenSending_ThenSetsBearerAuthorizationHeaderOnTheInnerRequest()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        using var tokenHandler = new StubHttpMessageHandler(_ => TokenResponse("token-xyz", 3600));
        using var innerHandler = new StubHttpMessageHandler(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var handler = new ClientCredentialsTokenHandler(MakeConfig(), tokenHandler) { InnerHandler = innerHandler };
        using var client = new HttpClient(handler);

        // Act
        await client.GetAsync(FhirResource("Patient/1"));

        // Assert
        captured.ShouldNotBeNull();
        captured!.Headers.Authorization.ShouldNotBeNull();
        captured.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        captured.Headers.Authorization.Parameter.ShouldBe("token-xyz");
    }

    [Fact]
    public async Task GivenAnAlreadyExpiredToken_WhenSendingAnotherRequest_ThenTokenIsRefetched()
    {
        // Arrange: expires_in below the 60s refresh buffer nets to an immediately-stale token, so
        // the second call is guaranteed to see it as expired without manipulating the clock.
        var tokenCalls = 0;
        using var tokenHandler = new StubHttpMessageHandler(_ => { tokenCalls++; return TokenResponse($"token-{tokenCalls}", 1); });
        using var innerHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var handler = new ClientCredentialsTokenHandler(MakeConfig(), tokenHandler) { InnerHandler = innerHandler };
        using var client = new HttpClient(handler);

        // Act
        await client.GetAsync(FhirResource("Patient/1"));
        await client.GetAsync(FhirResource("Patient/2"));

        // Assert
        tokenCalls.ShouldBe(2);
    }

    [Fact]
    public async Task GivenTokenEndpointFailure_WhenSending_ThenThrowsWithStatusAndBodySnippetButNeverTheSecret()
    {
        // Arrange
        using var tokenHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"invalid_client"}""", Encoding.UTF8, "application/json")
            });
        using var innerHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var handler = new ClientCredentialsTokenHandler(MakeConfig(), tokenHandler) { InnerHandler = innerHandler };
        using var client = new HttpClient(handler);

        // Act
        var ex = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync(FhirResource("Patient/1")));

        // Assert
        ex.Message.ShouldContain("401");
        ex.Message.ShouldContain("invalid_client");
        ex.Message.ShouldNotContain("client-secret");
    }
}
