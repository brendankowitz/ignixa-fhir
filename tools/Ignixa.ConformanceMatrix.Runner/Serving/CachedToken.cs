namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>A fetched OAuth2 access token and the instant it should be treated as expired (already netted against the refresh buffer).</summary>
internal sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
