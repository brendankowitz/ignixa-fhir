using System.Security.Claims;
using Ignixa.Api.OpenIddict.Configuration;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.OpenIddict.Services;

/// <summary>
/// Simple in-memory user service for development/testing with password flow.
/// WARNING: This is NOT suitable for production. Use a real identity provider.
/// </summary>
public sealed class DevelopmentUserService(IOptions<OpenIddictServerOptions> options)
{
    private readonly OpenIddictServerOptions _options = options.Value;

    /// <summary>
    /// Validates username and password against configured development users.
    /// </summary>
    public bool ValidateCredentials(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        var user = _options.DevelopmentUsers.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        return user is not null && user.Password == password;
    }

    /// <summary>
    /// Gets claims for a user to include in tokens.
    /// </summary>
    public IEnumerable<Claim> GetUserClaims(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        var user = _options.DevelopmentUsers.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            return [];
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.NameIdentifier, user.Username)
        };

        // Add FHIR-specific claims
        if (!string.IsNullOrEmpty(user.FhirUser))
        {
            claims.Add(new Claim("fhirUser", user.FhirUser));
        }

        if (!string.IsNullOrEmpty(user.PatientId))
        {
            claims.Add(new Claim("patient", user.PatientId));
        }

        // Add roles
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return claims;
    }
}
