using System.Collections.ObjectModel;

namespace Ignixa.Api.OpenIddict.Configuration;

/// <summary>
/// Configuration for a development/test user (password flow only).
/// </summary>
public sealed class DevelopmentUserOptions
{
    /// <summary>
    /// Username for login.
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// Password for login (stored in plain text - development only!).
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    /// FHIR User reference (e.g., "Practitioner/123").
    /// </summary>
    public string? FhirUser { get; set; }

    /// <summary>
    /// Patient context for patient-facing apps.
    /// </summary>
    public string? PatientId { get; set; }

    /// <summary>
    /// Roles to assign to this user's tokens.
    /// </summary>
    public Collection<string> Roles { get; } = [];
}
