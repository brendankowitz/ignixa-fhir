// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;

namespace Ignixa.FhirFakes;

/// <summary>
/// Helper methods for detecting and working with FHIR version differences.
/// Used by scenario states to generate version-appropriate field names and structures.
/// </summary>
internal static class FhirVersionHelper
{
    /// <summary>
    /// Checks if the schema is STU3.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>True if STU3, false otherwise.</returns>
    public static bool IsStu3(this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        return schemaProvider.Version == FhirVersion.Stu3;
    }

    /// <summary>
    /// Checks if the schema is R4 or later (R4, R4B, R5, R6).
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>True if R4 or later, false otherwise.</returns>
    public static bool IsR4OrLater(this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        return schemaProvider.Version >= FhirVersion.R4;
    }

    /// <summary>
    /// Checks if a specific property exists in a resource type definition.
    /// Useful for detecting version-specific fields.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="resourceType">The resource type name (e.g., "Immunization", "Patient").</param>
    /// <param name="propertyName">The property name to check (e.g., "protocolApplied", "vaccinationProtocol").</param>
    /// <returns>True if the property exists in the schema, false otherwise.</returns>
    public static bool HasProperty(this IFhirSchemaProvider schemaProvider, string resourceType, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(propertyName);

        var typeDefinition = schemaProvider.GetTypeDefinition(resourceType);
        return typeDefinition?.Children.Any(c => c.Info.Name == propertyName) ?? false;
    }

    /// <summary>
    /// Gets the correct field name for Immunization dose tracking based on FHIR version.
    /// STU3: "vaccinationProtocol"
    /// R4+: "protocolApplied"
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>The version-appropriate field name.</returns>
    public static string GetImmunizationProtocolFieldName(this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        return schemaProvider.IsStu3() ? "vaccinationProtocol" : "protocolApplied";
    }

    /// <summary>
    /// Gets the correct field name for dose number within Immunization protocol.
    /// STU3: "doseSequence" (integer)
    /// R4+: "doseNumberPositiveInt" (positiveInt) or "doseNumberString" (string)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>The version-appropriate field name for dose number.</returns>
    public static string GetImmunizationDoseNumberFieldName(this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        return schemaProvider.IsStu3() ? "doseSequence" : "doseNumberPositiveInt";
    }

    /// <summary>
    /// Gets the correct field name for series doses in Immunization protocol.
    /// STU3: "doseSequence" (combined with series)
    /// R4+: "seriesDosesPositiveInt" or "seriesDosesString"
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>The version-appropriate field name for series doses, or null if not applicable.</returns>
    public static string? GetImmunizationSeriesDosesFieldName(this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        // STU3 doesn't have a specific field for series doses - it's implied by the series name
        return schemaProvider.IsStu3() ? null : "seriesDosesPositiveInt";
    }

    /// <summary>
    /// Gets the correct field name for a choice type (e.g., medication[x], value[x], performed[x]).
    /// Tries each preferred suffix in order and returns the first match found in the schema.
    /// If no preferred suffix matches, returns any field starting with the base property name.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="resourceType">The resource type name (e.g., "MedicationRequest", "Observation").</param>
    /// <param name="basePropertyName">The base property name without suffix (e.g., "medication", "value", "performed").</param>
    /// <param name="preferredSuffixes">Ordered list of preferred suffixes to try (e.g., "CodeableConcept", "Reference").</param>
    /// <returns>The version-appropriate field name, or null if no matching field exists.</returns>
    /// <example>
    /// <code>
    /// // Get medication field for MedicationRequest, preferring CodeableConcept over Reference
    /// var field = schemaProvider.GetChoiceFieldName("MedicationRequest", "medication", 
    ///     "CodeableConcept", "Reference");
    /// // Returns "medicationCodeableConcept" if available, otherwise "medicationReference"
    /// </code>
    /// </example>
    public static string? GetChoiceFieldName(
        this IFhirSchemaProvider schemaProvider,
        string resourceType,
        string basePropertyName,
        params string[] preferredSuffixes)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(basePropertyName);

        var typeDefinition = schemaProvider.GetTypeDefinition(resourceType);
        if (typeDefinition is null)
        {
            return null;
        }

        // Try each preferred suffix in order
        foreach (var suffix in preferredSuffixes)
        {
            var fieldName = $"{basePropertyName}{suffix}";
            if (typeDefinition.Children.Any(c => c.Info.Name == fieldName))
            {
                return fieldName;
            }
        }

        // Fall back to any field starting with basePropertyName
        return typeDefinition.Children
            .FirstOrDefault(c => c.Info.Name.StartsWith(basePropertyName, StringComparison.OrdinalIgnoreCase))
            ?.Info.Name;
    }

    /// <summary>
    /// Checks if a specific property is required in a resource type definition.
    /// Useful for conditionally setting fields based on version requirements.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="resourceType">The resource type name (e.g., "AllergyIntolerance", "Observation").</param>
    /// <param name="propertyName">The property name to check (e.g., "clinicalStatus", "verificationStatus").</param>
    /// <returns>True if the property is required in the schema, false otherwise.</returns>
    /// <example>
    /// <code>
    /// // Check if clinicalStatus is required (it is in R4+ but not STU3)
    /// if (schemaProvider.IsRequired("AllergyIntolerance", "clinicalStatus"))
    /// {
    ///     node["clinicalStatus"] = ...;
    /// }
    /// </code>
    /// </example>
    public static bool IsRequired(
        this IFhirSchemaProvider schemaProvider,
        string resourceType,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(propertyName);

        var typeDefinition = schemaProvider.GetTypeDefinition(resourceType);
        var element = typeDefinition?.Children
            .FirstOrDefault(c => c.Info.Name == propertyName);
        return element?.IsRequired ?? false;
    }

    /// <summary>
    /// Checks if a specific property is marked as being in the summary.
    /// Summary elements are considered core/important elements that should typically be populated.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="resourceType">The resource type name.</param>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>True if the property is in the summary, false otherwise.</returns>
    public static bool IsInSummary(
        this IFhirSchemaProvider schemaProvider,
        string resourceType,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(propertyName);

        var typeDefinition = schemaProvider.GetTypeDefinition(resourceType);
        var element = typeDefinition?.Children
            .FirstOrDefault(c => c.Info.Name == propertyName);
        return element?.InSummary ?? false;
    }
}
