using System.Collections.ObjectModel;

namespace Ignixa.Api.OpenIddict.Configuration;

/// <summary>
/// Configuration options for the embedded OpenIddict server.
/// </summary>
public sealed class OpenIddictServerOptions
{
    public const string SectionName = "OpenIddict";

    /// <summary>
    /// Whether the embedded OpenIddict server is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The issuer URI for tokens. Defaults to the application's base URL.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// The audience for tokens.
    /// </summary>
    public string Audience { get; set; } = "fhir-api";

    /// <summary>
    /// Whether to use in-memory storage (for development) or SQL Server.
    /// </summary>
    public bool UseInMemoryStorage { get; set; } = true;

    /// <summary>
    /// Connection string for SQL Server storage (when UseInMemoryStorage is false).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Whether to disable HTTPS requirement (development only).
    /// </summary>
    public bool DisableHttpsRequirement { get; set; }

    /// <summary>
    /// Whether to disable access token encryption (for easier debugging).
    /// </summary>
    public bool DisableAccessTokenEncryption { get; set; } = true;

    /// <summary>
    /// Pre-configured client applications.
    /// </summary>
    public Collection<ClientApplicationOptions> ClientApplications { get; } = [];

    /// <summary>
    /// Pre-configured development users (for password flow testing).
    /// </summary>
    public Collection<DevelopmentUserOptions> DevelopmentUsers { get; } = [];
}
