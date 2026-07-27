using System.Net.Http.Headers;
using System.Text.Json;

namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>
/// Applies OAuth2 client-credentials bearer tokens to outgoing FHIR requests. The token is cached
/// until 60 seconds before its reported expiry and refreshed via a single-flight
/// (<see cref="SemaphoreSlim"/>-guarded) call, so concurrent requests near expiry share one token
/// refresh instead of each firing their own against the token endpoint.
/// </summary>
internal sealed class ClientCredentialsTokenHandler : DelegatingHandler
{
    // Refresh this far ahead of the reported expiry so an in-flight request never presents a token
    // that expires mid-call.
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    private readonly TokenClientConfiguration _configuration;
    private readonly HttpClient _tokenClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CachedToken? _cachedToken;

    public ClientCredentialsTokenHandler(TokenClientConfiguration configuration, HttpMessageHandler? tokenHandler = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        // A caller-supplied handler (tests) is caller-owned; only a self-created default handler is
        // disposed by this instance.
        _tokenClient = tokenHandler is not null ? new HttpClient(tokenHandler, disposeHandler: false) : new HttpClient();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is { } cached && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.AccessToken;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedToken is { } stillCached && stillCached.ExpiresAt > DateTimeOffset.UtcNow)
                return stillCached.AccessToken;

            var fetched = await FetchTokenAsync(cancellationToken).ConfigureAwait(false);
            _cachedToken = fetched;
            return fetched.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<CachedToken> FetchTokenAsync(CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _configuration.ClientId,
            ["client_secret"] = _configuration.ClientSecret
        };
        if (!string.IsNullOrEmpty(_configuration.Scopes))
            form["scope"] = _configuration.Scopes;

        using var content = new FormUrlEncodedContent(form);
        using var response = await _tokenClient
            .PostAsync(new Uri(_configuration.TokenUrl), content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Status + a body snippet is enough to diagnose a token endpoint misconfiguration;
            // client_id/client_secret are never included here.
            var snippet = body.Length > 500 ? body[..500] : body;
            throw new HttpRequestException($"Token endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}");
        }

        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("access_token", out var accessTokenProperty)
            || accessTokenProperty.GetString() is not { } accessToken)
            throw new HttpRequestException("Token endpoint response did not include an access_token");

        var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expiresProperty)
            && expiresProperty.TryGetInt32(out var seconds)
                ? seconds
                : 300;

        var ttl = TimeSpan.FromSeconds(expiresIn) - ExpiryBuffer;
        var expiresAt = DateTimeOffset.UtcNow + (ttl > TimeSpan.Zero ? ttl : TimeSpan.Zero);
        return new CachedToken(accessToken, expiresAt);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tokenClient.Dispose();
            _refreshLock.Dispose();
        }
        base.Dispose(disposing);
    }
}
