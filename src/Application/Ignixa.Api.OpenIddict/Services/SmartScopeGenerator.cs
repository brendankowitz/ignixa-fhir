using System.Text.RegularExpressions;

namespace Ignixa.Api.OpenIddict.Services;

/// <summary>
/// Generates SMART on FHIR v2 scope strings for OpenIddict registration.
/// Produces all valid scope combinations for resource types.
/// </summary>
/// <remarks>
/// This generator is version-agnostic. Resource types should be provided from
/// <see cref="Ignixa.Specification.IFhirSchemaProvider.ResourceTypeNames"/> for
/// proper multi-version FHIR support.
/// </remarks>
public static partial class SmartScopeGenerator
{
    private static readonly string[] ScopeTypes = ["patient", "user", "system", "practitioner"];

    /// <summary>
    /// Permission string combinations in canonical CRUDS order.
    /// </summary>
    private static readonly string[] PermissionCombinations =
    [
        "c", "r", "u", "d", "s",
        "cr", "cu", "cd", "cs", "ru", "rd", "rs", "ud", "us", "ds",
        "cru", "crd", "crs", "cud", "cus", "cds", "rud", "rus", "rds", "uds",
        "crud", "crus", "crds", "cuds", "ruds",
        "cruds"
    ];

    /// <summary>
    /// Standard OpenID Connect and SMART special scopes.
    /// </summary>
    private static readonly string[] SpecialScopes =
    [
        "openid",
        "profile",
        "email",
        "fhirUser",
        "offline_access",
        "launch",
        "launch/patient",
        "launch/encounter"
    ];

    // Regex for matching SMART v2 scope with optional search constraints
    [GeneratedRegex(
        @"^(patient|user|system|practitioner)/([A-Za-z*]+)\.(c?r?u?d?s?)(\?.*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SmartV2ScopeRegex();

    // Regex for matching SMART v1 scope (read/write/*)
    [GeneratedRegex(
        @"^(patient|user|system)/([A-Za-z*]+)\.(read|write|\*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SmartV1ScopeRegex();

    /// <summary>
    /// Generates all valid SMART on FHIR v2 scopes for registration.
    /// </summary>
    /// <param name="resourceTypes">
    /// Resource type names from IFhirSchemaProvider.ResourceTypeNames.
    /// Pass null or empty to generate only wildcard and special scopes.
    /// </param>
    /// <returns>Collection of all valid SMART scopes.</returns>
    public static IEnumerable<string> GenerateAllScopes(IEnumerable<string>? resourceTypes = null)
    {
        // Include special scopes
        foreach (var scope in SpecialScopes)
        {
            yield return scope;
        }

        // Generate wildcard scopes for each context type
        foreach (var scopeType in ScopeTypes)
        {
            foreach (var perm in PermissionCombinations)
            {
                yield return $"{scopeType}/*.{perm}";
            }
        }

        // Generate resource-specific scopes if resource types provided
        if (resourceTypes is null)
        {
            yield break;
        }

        foreach (var scopeType in ScopeTypes)
        {
            foreach (var resourceType in resourceTypes)
            {
                foreach (var perm in PermissionCombinations)
                {
                    yield return $"{scopeType}/{resourceType}.{perm}";
                }
            }
        }
    }

    /// <summary>
    /// Generates all SMART scopes for multiple FHIR versions.
    /// Combines resource types from all schema providers to ensure
    /// all versions are supported.
    /// </summary>
    /// <param name="resourceTypeSets">
    /// Resource type sets from multiple IFhirSchemaProvider instances.
    /// </param>
    /// <returns>Deduplicated collection of all valid SMART scopes.</returns>
    public static IEnumerable<string> GenerateAllScopesForVersions(
        params IEnumerable<string>[] resourceTypeSets)
    {
        // Union all resource types across versions
        var allResourceTypes = resourceTypeSets
            .SelectMany(r => r)
            .Distinct(StringComparer.Ordinal);

        return GenerateAllScopes(allResourceTypes);
    }

    /// <summary>
    /// Common resource types present across all FHIR versions (R4, R4B, R5, R6).
    /// Used for generating a minimal scope set when full schema providers aren't available.
    /// </summary>
    private static readonly string[] CommonResourceTypes =
    [
        "Patient", "Observation", "Condition", "Procedure", "Encounter",
        "MedicationRequest", "AllergyIntolerance", "Immunization",
        "DiagnosticReport", "DocumentReference", "Practitioner", "Organization",
        "Location", "Device", "Medication", "CarePlan", "CareTeam",
        "Composition", "Consent", "Coverage", "Goal", "Group", "Provenance"
    ];

    /// <summary>
    /// Generates common SMART scopes (subset for typical use).
    /// Uses a minimal set of resource types common to all FHIR versions.
    /// </summary>
    /// <returns>Collection of commonly used SMART scopes.</returns>
    public static IEnumerable<string> GenerateCommonScopes()
    {
        // Special scopes
        foreach (var scope in SpecialScopes)
        {
            yield return scope;
        }

        // Wildcard scopes
        foreach (var scopeType in ScopeTypes)
        {
            yield return $"{scopeType}/*.rs";
            yield return $"{scopeType}/*.cruds";
        }

        // Common resource types with typical permissions
        foreach (var scopeType in ScopeTypes)
        {
            foreach (var resource in CommonResourceTypes)
            {
                yield return $"{scopeType}/{resource}.rs";
                yield return $"{scopeType}/{resource}.cruds";
            }
        }
    }

    /// <summary>
    /// Validates whether a scope string is a valid SMART on FHIR scope.
    /// Supports both v1 and v2 formats, as well as special scopes.
    /// </summary>
    /// <param name="scope">The scope string to validate.</param>
    /// <returns>True if the scope is valid.</returns>
    public static bool IsValidSmartScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }

        // Check special scopes
        if (SpecialScopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check SMART v2 format
        var v2Match = SmartV2ScopeRegex().Match(scope);
        if (v2Match.Success)
        {
            var permissionStr = v2Match.Groups[3].Value;
            return IsValidPermissionOrder(permissionStr);
        }

        // Check SMART v1 format (legacy)
        return SmartV1ScopeRegex().IsMatch(scope);
    }

    /// <summary>
    /// Normalizes a scope string for validation by stripping search constraints.
    /// OpenIddict validates registered scopes without query parameters.
    /// </summary>
    /// <param name="scope">The scope string, possibly with search constraints.</param>
    /// <returns>The normalized scope without search constraints.</returns>
    public static string NormalizeScopeForValidation(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return scope;
        }

        // Strip query parameters from SMART v2 scopes
        var queryIndex = scope.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? scope[..queryIndex] : scope;
    }

    /// <summary>
    /// Validates that permissions are in correct CRUDS order.
    /// SMART v2 requires permissions in the order: c, r, u, d, s.
    /// </summary>
    private static bool IsValidPermissionOrder(string permissions)
    {
        if (string.IsNullOrEmpty(permissions))
        {
            return false;
        }

        const string validOrder = "CRUDS";
        int lastIndex = -1;
        foreach (char c in permissions.ToUpperInvariant())
        {
            int currentIndex = validOrder.IndexOf(c, StringComparison.Ordinal);
            if (currentIndex == -1 || currentIndex <= lastIndex)
            {
                return false;
            }
            lastIndex = currentIndex;
        }

        return true;
    }
}
