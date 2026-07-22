namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>
/// OAuth2 client-credentials configuration for <see cref="ClientCredentialsTokenHandler"/>. Read via
/// <see cref="FromEnvironment"/> rather than each collaborator reading process environment variables
/// directly, so tests can construct one without touching real environment state.
/// </summary>
internal sealed record TokenClientConfiguration(string TokenUrl, string ClientId, string ClientSecret, string? Scopes)
{
    /// <summary>
    /// Returns <see langword="null"/> when FHIR_TOKEN_URL is unset — token auth is opt-in; an unset
    /// token URL means no client-credentials handler should be installed at all, and a static
    /// --auth-header (or anonymous access) applies instead.
    /// </summary>
    public static TokenClientConfiguration? FromEnvironment()
    {
        var tokenUrl = Environment.GetEnvironmentVariable("FHIR_TOKEN_URL");
        if (string.IsNullOrWhiteSpace(tokenUrl))
            return null;

        return new TokenClientConfiguration(
            tokenUrl,
            Environment.GetEnvironmentVariable("FHIR_CLIENT_ID") ?? string.Empty,
            Environment.GetEnvironmentVariable("FHIR_CLIENT_SECRET") ?? string.Empty,
            Environment.GetEnvironmentVariable("FHIR_SCOPES"));
    }
}
