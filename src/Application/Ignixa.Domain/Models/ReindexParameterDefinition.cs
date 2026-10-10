namespace Ignixa.Domain.Models;

/// <param name="SearchParamId">The conformance (logical) id; not a tenant catalog id.</param>
/// <param name="OverridesCanonical">
/// The canonical whose storage identity this parameter inherits, or null when it owns its own.
/// </param>
public sealed record ReindexParameterDefinition(
    string Canonical,
    string Code,
    string ResourceType,
    int SearchParamId,
    long ActivationEventId,
    IReadOnlyList<string> AffectedResourceTypes,
    string? OverridesCanonical = null)
{
    /// <summary>
    /// The URI the parameter's index rows are stored under in each tenant's catalog, as extraction resolves it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string StorageCanonical => OverridesCanonical ?? Canonical;
}
