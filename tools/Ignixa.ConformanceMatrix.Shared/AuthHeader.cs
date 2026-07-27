using System.Net.Http.Headers;

namespace Ignixa.ConformanceMatrix;

/// <summary>
/// Parses and applies <c>--auth-header</c> values. Compile-linked into both the
/// Ignixa.ConformanceMatrix.Cli and Ignixa.ConformanceMatrix.Runner projects so the two tools keep
/// identical parsing rules without a shared assembly.
/// </summary>
internal static class AuthHeader
{
    // An HTTP header name cannot contain whitespace, so text before the first colon that has none
    // is a header name and anything else is a bare credential for Authorization. This holds for
    // any scheme — Negotiate, NTLM, AWS4-HMAC-SHA256 — without enumerating them.
    internal static (string Name, string Value) Parse(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
            return ("Authorization", string.Empty);

        var separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex > 0)
        {
            var name = trimmed[..separatorIndex].Trim();
            if (name.Length > 0 && !name.Any(char.IsWhiteSpace))
                return (name, trimmed[(separatorIndex + 1)..].Trim());
        }

        return ("Authorization", trimmed);
    }

    internal static string? Apply(HttpClient httpClient, string? authHeader)
    {
        if (authHeader is null)
            return null;

        var (name, value) = Parse(authHeader);

        if (string.IsNullOrWhiteSpace(value))
            return $"--auth-header '{authHeader}' resolves to no header value; expected 'Bearer <token>' or 'Header-Name: <value>'. If an environment variable expands to empty, omit the flag instead of passing a blank value.";

        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            && AuthenticationHeaderValue.TryParse(value, out var parsed))
        {
            httpClient.DefaultRequestHeaders.Authorization = parsed;
            return null;
        }

        // TryAddWithoutValidation returns false (it does not throw) when the name is not a valid
        // HTTP token, which would otherwise drop the credential without a trace.
        if (!httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value))
            return $"--auth-header name '{name}' is not a valid HTTP header name.";

        return null;
    }
}
