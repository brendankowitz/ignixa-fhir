using System.Collections.ObjectModel;

namespace Ignixa.Api.OpenIddict.Configuration;

/// <summary>
/// Configuration for a pre-registered OAuth client application.
/// </summary>
public sealed class ClientApplicationOptions
{
    /// <summary>
    /// The client ID.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// The client secret (for confidential clients).
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Display name for the client.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Allowed redirect URIs for authorization code flow.
    /// </summary>
    public Collection<string> RedirectUris { get; } = [];

    /// <summary>
    /// Allowed post-logout redirect URIs.
    /// </summary>
    public Collection<string> PostLogoutRedirectUris { get; } = [];

    /// <summary>
    /// Allowed grant types (authorization_code, client_credentials, password).
    /// </summary>
    public Collection<string> GrantTypes { get; } = ["client_credentials"];

    /// <summary>
    /// Allowed scopes for this client.
    /// </summary>
    public Collection<string> Scopes { get; } = [];

    /// <summary>
    /// Whether this is a public client (no secret required).
    /// </summary>
    public bool IsPublicClient { get; set; }

    /// <summary>
    /// FHIR roles to assign to tokens from this client.
    /// </summary>
    public Collection<string> Roles { get; } = [];
}
