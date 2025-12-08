// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirFakes;

/// <summary>
/// Maps version-specific field name overrides for FHIR resources.
/// Uses R4+ normative field names by default, with STU3 overrides where they differ.
/// </summary>
internal static class VersionFieldOverrides
{
    /// <summary>
    /// Override mappings: (FhirVersion, ResourceType, NormativeFieldName) -> ActualFieldName
    /// Only includes entries where the field name differs from R4+ normative.
    /// </summary>
    private static readonly Dictionary<(FhirVersion, string, string), string> Overrides = new()
    {
        // STU3-specific overrides (R4+ field names remain the same)
        // Format: (FhirVersion.Stu3, "ResourceType", "normativeFieldName") -> "stu3FieldName"

        // Add overrides here as needed:
        // Example: (FhirVersion.Stu3, "Observation", "effectiveDateTime") -> "effectiveDateTime",
    };

    /// <summary>
    /// Gets the actual field name for a given FHIR version, applying overrides where necessary.
    /// </summary>
    /// <param name="version">The FHIR version</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Observation", "Procedure")</param>
    /// <param name="normativeFieldName">The R4+ normative field name (e.g., "effectiveDateTime")</param>
    /// <returns>The version-appropriate field name</returns>
    public static string GetFieldName(FhirVersion version, string resourceType, string normativeFieldName)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(normativeFieldName);

        var key = (version, resourceType, normativeFieldName);
        return Overrides.TryGetValue(key, out var overrideName)
            ? overrideName
            : normativeFieldName;
    }
}
