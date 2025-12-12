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
        // Format: { (FhirVersion.Stu3, "ResourceType", "normativeFieldName"), "stu3FieldName" }

        // MedicationRequest: STU3 uses bare "medication" field instead of choice types
        { (FhirVersion.Stu3, "MedicationRequest", "medicationCodeableConcept"), "medication" },

        // Encounter: STU3 uses "reason" instead of "reasonCode" (and reason is a CodeableConcept array)
        { (FhirVersion.Stu3, "Encounter", "reasonCode"), "reason" },

        // Procedure: STU3 uses "performed[x]" as choice element (not "performedPeriod" directly)
        { (FhirVersion.Stu3, "Procedure", "performedPeriod"), "performedPeriod" },

        // Procedure: STU3 uses "context" instead of "encounter" for encounter reference
        { (FhirVersion.Stu3, "Procedure", "encounter"), "context" },

        // Procedure.performer: STU3 uses "role" instead of "function"
        { (FhirVersion.Stu3, "Procedure.performer", "function"), "role" },

        // Condition: STU3 uses "context" instead of "encounter" for encounter reference
        { (FhirVersion.Stu3, "Condition", "encounter"), "context" },

        // Condition: STU3 uses "assertedDate" instead of "recordedDate"
        { (FhirVersion.Stu3, "Condition", "recordedDate"), "assertedDate" },

        // Observation: STU3 uses "context" instead of "encounter" for encounter reference
        { (FhirVersion.Stu3, "Observation", "encounter"), "context" },

        // AllergyIntolerance: STU3 doesn't have "encounter" field (it was added in R4)
        // Map to empty to signal it should be skipped in STU3
        { (FhirVersion.Stu3, "AllergyIntolerance", "encounter"), "" },

        // DiagnosticReport: STU3 uses "context" instead of "encounter"
        { (FhirVersion.Stu3, "DiagnosticReport", "encounter"), "context" },

        // Immunization: STU3 uses "date" instead of "occurrenceDateTime"
        { (FhirVersion.Stu3, "Immunization", "occurrenceDateTime"), "date" },

        // AllergyIntolerance: STU3 uses "assertedDate" instead of "recordedDate"
        { (FhirVersion.Stu3, "AllergyIntolerance", "recordedDate"), "assertedDate" },

        // R5-specific overrides (breaking changes from R4)

        // Encounter: R5 uses "actualPeriod" instead of "period"
        { (FhirVersion.R5, "Encounter", "period"), "actualPeriod" },

        // Encounter: R5 uses "reason" with different structure (backbone element) instead of "reasonCode"
        // Map to empty to signal the state should skip this in R5 (reason structure is complex)
        { (FhirVersion.R5, "Encounter", "reasonCode"), "" },

        // Encounter: R5 uses "completed" instead of "finished" for status
        // Note: This isn't a field override but a code mapping issue handled in BindingCodeMapper
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
