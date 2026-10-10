using Ignixa.Serialization;

namespace Ignixa.Domain.Models;

/// <param name="SearchParamId">The conformance (logical) id; not a tenant catalog id.</param>
/// <param name="OverridesCanonical">
/// The canonical whose storage identity this parameter inherits, or null when it owns its own.
/// </param>
/// <param name="FhirVersion">
/// The FHIR version ("4.0", "5.0", ...) whose tenants the parameter applies to, or null when it applies to
/// every tenant (a definition recorded before versions were carried).
/// </param>
public sealed record ReindexParameterDefinition(
    string Canonical,
    string Code,
    string ResourceType,
    int SearchParamId,
    long ActivationEventId,
    IReadOnlyList<string> AffectedResourceTypes,
    string? OverridesCanonical = null,
    string? FhirVersion = null)
{
    /// <summary>
    /// The URI the parameter's index rows are stored under in each tenant's catalog, as extraction resolves it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string StorageCanonical => OverridesCanonical ?? Canonical;

    /// <summary>
    /// Whether a tenant on <paramref name="tenantFhirVersion"/> indexes this parameter.
    /// </summary>
    public bool AppliesToTenant(string tenantFhirVersion) =>
        FhirVersion is null ||
        FhirSpecificationExtensions.FromVersionString(FhirVersion) ==
        FhirSpecificationExtensions.FromVersionString(tenantFhirVersion);
}
