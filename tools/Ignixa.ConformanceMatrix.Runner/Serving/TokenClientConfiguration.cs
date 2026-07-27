namespace Ignixa.ConformanceMatrix.Runner.Serving;

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

    /// <summary>
    /// Returns an error message when the configuration cannot possibly produce a token — an invalid
    /// token URL or blank credentials — so serve fails at startup rather than on the first /run
    /// call. A well-formed but wrong secret still only surfaces on first use; that failure is
    /// logged by the /run handler.
    /// </summary>
    public string? Validate()
    {
        if (!Uri.TryCreate(TokenUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return $"FHIR_TOKEN_URL is not an absolute http(s) URL: '{TokenUrl}'";

        if (string.IsNullOrWhiteSpace(ClientId))
            return "FHIR_TOKEN_URL is set but FHIR_CLIENT_ID is empty — client-credentials auth needs both FHIR_CLIENT_ID and FHIR_CLIENT_SECRET";

        if (string.IsNullOrWhiteSpace(ClientSecret))
            return "FHIR_TOKEN_URL is set but FHIR_CLIENT_SECRET is empty — client-credentials auth needs both FHIR_CLIENT_ID and FHIR_CLIENT_SECRET";

        return null;
    }
}
