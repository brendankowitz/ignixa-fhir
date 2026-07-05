// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios.Codes;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.FhirFakes;

/// <summary>
/// Maps FHIR value set bindings to predefined code constants.
/// This enables the faker to generate terminology-correct codes based on
/// binding information from the FHIR schema rather than relying solely on
/// property name heuristics.
/// </summary>
/// <remarks>
/// BINDING STRENGTH SEMANTICS:
/// - required: The code MUST come from the specified value set
/// - extensible: The code SHOULD come from the value set, but can use other codes if needed
/// - preferred: The code SHOULD come from the value set, but alternatives are acceptable
/// - example: The value set is just an example, any appropriate code is fine
///
/// This mapper provides codes for common FHIR value sets. When generating fake data,
/// the faker can query this mapper to get appropriate codes for elements with bindings,
/// ensuring generated resources use realistic, valid terminology.
///
/// NOTE: Thread-safe via benign-race lazy caching — concurrent first-access calls may each compute a
/// code cache once, but all results are identical, so no locking is needed. The IValueSetProvider must
/// be passed to methods that need version-specific code lookup to ensure correct version handling
/// when multiple FHIR versions are used concurrently (e.g., in test scenarios).
/// </remarks>
internal static class BindingCodeMapper
{

    /// <summary>
    /// Known binding strength values from FHIR specification.
    /// </summary>
    public static class BindingStrength
    {
        public const string Required = "required";
        public const string Extensible = "extensible";
        public const string Preferred = "preferred";
        public const string Example = "example";
    }

    // Cached code arrays for value sets - loaded lazily from static properties.
    // ImmutableArray<T> (not T[]) so the cache cannot be mutated through a downcast of the public return value.
    // Default (IsDefault == true) is the "not yet computed" sentinel.
    private static ImmutableArray<FhirCode> _allergenCodes;
    private static ImmutableArray<FhirCode> _immunizationCodes;
    private static ImmutableArray<FhirCode> _labObservationCodes;
    private static ImmutableArray<FhirCode> _procedureCodes;
    private static ImmutableArray<FhirCode> _vitalSignCodes;
    private static ImmutableArray<FhirCode> _diagnosticReportCodes;
    private static ImmutableArray<FhirCode> _medicationCodes;
    private static ImmutableArray<FhirCode> _conditionCodes;
    private static ImmutableArray<FhirCode> _encounterTypeCodes;
    private static ImmutableArray<FhirCode> _observationCodes;

    /// <summary>
    /// Attempts to get predefined codes for a value set URI.
    /// </summary>
    /// <param name="valueSetUri">The canonical URL of the value set from the binding.</param>
    /// <param name="valueSetProvider">The version-specific value set provider for FHIR spec codes.</param>
    /// <param name="codes">The array of predefined codes if found.</param>
    /// <returns>True if codes were found for this value set, false otherwise.</returns>
    public static bool TryGetCodesForValueSet(string? valueSetUri, IValueSetProvider? valueSetProvider, out ImmutableArray<FhirCode> codes)
    {
        if (string.IsNullOrEmpty(valueSetUri))
        {
            codes = [];
            return false;
        }

        codes = GetCodesForValueSetInternal(valueSetUri, valueSetProvider);
        return codes.Length > 0;
    }

    /// <summary>
    /// Theme-aware variant: filters the value set's codes to the given clinical domain, falling back
    /// to the full pool when the theme is <see cref="ClinicalDomain.Unspecified"/> or has no tagged match.
    /// </summary>
    public static bool TryGetCodesForValueSet(
        string? valueSetUri, IValueSetProvider? valueSetProvider, ClinicalDomain theme, out ImmutableArray<FhirCode> codes)
    {
        if (!TryGetCodesForValueSet(valueSetUri, valueSetProvider, out var all))
        {
            codes = [];
            return false;
        }

        if (theme == ClinicalDomain.Unspecified)
        {
            codes = all;
            return true;
        }

        var themed = all.Where(c => c.Domain == theme).ToImmutableArray();
        codes = themed.Length > 0 ? themed : all;
        return true;
    }

    /// <summary>
    /// Gets codes for a value set URI, returning an empty array if no mapping exists.
    /// </summary>
    /// <param name="valueSetUri">The canonical URL of the value set.</param>
    /// <param name="valueSetProvider">The version-specific value set provider.</param>
    private static ImmutableArray<FhirCode> GetCodesForValueSetInternal(string valueSetUri, IValueSetProvider? valueSetProvider)
    {
        // Normalize the URI (remove version suffixes like |4.0.1)
        var normalizedUri = NormalizeValueSetUri(valueSetUri);

        // PRIORITY 1: Check clinical terminology codes FIRST (curated for realistic test data)
        // These take precedence over FHIR package codes because they're specifically designed
        // for test data generation with real-world clinical terminology.
        var clinicalCodes = normalizedUri switch
        {
            // Observation Codes (LOINC) - http://hl7.org/fhir/ValueSet/observation-codes
            "http://hl7.org/fhir/ValueSet/observation-codes" => GetAllLabAndVitalSignCodes(),

            // Procedure Codes (SNOMED CT) - http://hl7.org/fhir/ValueSet/procedure-code
            "http://hl7.org/fhir/ValueSet/procedure-code" => GetAllProcedureCodes(),

            // AllergyIntolerance Code (SNOMED CT) - http://hl7.org/fhir/ValueSet/allergyintolerance-code
            "http://hl7.org/fhir/ValueSet/allergyintolerance-code" => GetAllAllergenCodes(),

            // Immunization Vaccine Codes (CVX) - http://hl7.org/fhir/ValueSet/vaccine-code
            "http://hl7.org/fhir/ValueSet/vaccine-code" => GetAllImmunizationCodes(),

            // Medication Codes (RxNorm) - http://hl7.org/fhir/ValueSet/medication-codes
            "http://hl7.org/fhir/ValueSet/medication-codes" => GetAllMedicationCodes(),

            // Condition Code (SNOMED CT) - http://hl7.org/fhir/ValueSet/condition-code
            "http://hl7.org/fhir/ValueSet/condition-code" => GetAllConditionCodes(),

            // Diagnostic Report Codes (LOINC) - http://hl7.org/fhir/ValueSet/report-codes
            "http://hl7.org/fhir/ValueSet/report-codes" => GetAllDiagnosticReportCodes(),

            // Encounter Type - http://hl7.org/fhir/ValueSet/encounter-type
            "http://hl7.org/fhir/ValueSet/encounter-type" => GetAllEncounterTypeCodes(),

            // Vital Signs Profile codes (subset of observation-codes)
            "http://hl7.org/fhir/ValueSet/observation-vitalsignresult" => GetAllVitalSignCodes(),

            _ => []
        };

        if (clinicalCodes.Length > 0)
        {
            return clinicalCodes;
        }

        // PRIORITY 2: Fall back to IValueSetProvider (FHIR package valuesets)
        // Use FHIR spec codes for administrative/structural valuesets that don't have
        // curated clinical codes (e.g., administrative-gender, name-use, etc.)
        if (valueSetProvider is not null)
        {
            var codes = valueSetProvider.GetCodes(normalizedUri);
            if (codes is not null)
            {
                // Convert from Ignixa.Abstractions.FhirCode to Ignixa.FhirFakes.Scenarios.Codes.FhirCode
                return codes.Select(c => new FhirCode(c.System, c.Code, c.Display)).ToImmutableArray();
            }
        }

        // No codes found
        return [];
    }

    /// <summary>
    /// Normalizes value set URIs by removing version suffixes.
    /// Example: "http://hl7.org/fhir/ValueSet/administrative-gender|4.0.1" -> "http://hl7.org/fhir/ValueSet/administrative-gender"
    /// </summary>
    private static string NormalizeValueSetUri(string valueSetUri)
    {
        var pipeIndex = valueSetUri.IndexOf('|', StringComparison.Ordinal);
        return pipeIndex > 0 ? valueSetUri[..pipeIndex] : valueSetUri;
    }

    /// <summary>
    /// Gets all allergen codes from the Allergens class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllAllergenCodes()
    {
        if (_allergenCodes.IsDefault)
        {
            _allergenCodes = GetCodesFromStaticProperties(typeof(Allergens));
        }
        return _allergenCodes;
    }

    /// <summary>
    /// Gets all immunization (vaccine) codes from the Immunizations class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllImmunizationCodes()
    {
        if (_immunizationCodes.IsDefault)
        {
            _immunizationCodes = GetCodesFromStaticProperties(typeof(Immunizations));
        }
        return _immunizationCodes;
    }

    /// <summary>
    /// Gets all laboratory observation codes from the LabObservations class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllLabObservationCodes()
    {
        if (_labObservationCodes.IsDefault)
        {
            _labObservationCodes = GetCodesFromStaticProperties(typeof(LabObservations));
        }
        return _labObservationCodes;
    }

    /// <summary>
    /// Gets all procedure codes from the Procedures class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllProcedureCodes()
    {
        if (_procedureCodes.IsDefault)
        {
            _procedureCodes = GetCodesFromStaticProperties(typeof(Procedures));
        }
        return _procedureCodes;
    }

    /// <summary>
    /// Gets all vital sign observation codes from the VitalSigns class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllVitalSignCodes()
    {
        if (_vitalSignCodes.IsDefault)
        {
            _vitalSignCodes = GetCodesFromStaticProperties(typeof(VitalSigns));
        }
        return _vitalSignCodes;
    }

    /// <summary>
    /// Gets all diagnostic report type codes from the DiagnosticReports class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllDiagnosticReportCodes()
    {
        if (_diagnosticReportCodes.IsDefault)
        {
            _diagnosticReportCodes = GetCodesFromStaticProperties(typeof(DiagnosticReports));
        }
        return _diagnosticReportCodes;
    }

    /// <summary>
    /// Gets all medication codes from the FhirCode.Medications nested class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllMedicationCodes()
    {
        if (_medicationCodes.IsDefault)
        {
            _medicationCodes = GetCodesFromStaticFields(typeof(FhirCode.Medications));
        }
        return _medicationCodes;
    }

    /// <summary>
    /// Gets all condition codes from the FhirCode.Conditions nested class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllConditionCodes()
    {
        if (_conditionCodes.IsDefault)
        {
            _conditionCodes = GetCodesFromStaticFields(typeof(FhirCode.Conditions));
        }
        return _conditionCodes;
    }

    /// <summary>
    /// Gets all encounter type codes from the FhirCode.EncounterTypes nested class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllEncounterTypeCodes()
    {
        if (_encounterTypeCodes.IsDefault)
        {
            _encounterTypeCodes = GetCodesFromStaticFields(typeof(FhirCode.EncounterTypes));
        }
        return _encounterTypeCodes;
    }

    /// <summary>
    /// Gets all observation codes from the FhirCode.Observations nested class.
    /// </summary>
    public static ImmutableArray<FhirCode> GetAllObservationCodes()
    {
        if (_observationCodes.IsDefault)
        {
            _observationCodes = GetCodesFromStaticFields(typeof(FhirCode.Observations));
        }
        return _observationCodes;
    }

    /// <summary>
    /// Gets combined lab observation and vital sign codes.
    /// </summary>
    private static ImmutableArray<FhirCode> GetAllLabAndVitalSignCodes()
    {
        var labCodes = GetAllLabObservationCodes();
        var vitalCodes = GetAllVitalSignCodes();
        return [.. labCodes, .. vitalCodes];
    }

    /// <summary>
    /// Extracts FhirCode values from all public static properties of a type.
    /// </summary>
    private static ImmutableArray<FhirCode> GetCodesFromStaticProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(FhirCode))
            .Select(p => (FhirCode)p.GetValue(null)!)
            .ToImmutableArray();
    }

    /// <summary>
    /// Extracts FhirCode values from all public static fields of a type.
    /// </summary>
    private static ImmutableArray<FhirCode> GetCodesFromStaticFields(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(FhirCode))
            .Select(f => (FhirCode)f.GetValue(null)!)
            .ToImmutableArray();
    }

    /// <summary>
    /// Checks if the binding strength requires strict adherence to the value set.
    /// </summary>
    /// <param name="strength">The binding strength value.</param>
    /// <returns>True if the binding is required or extensible.</returns>
    public static bool IsStrictBinding(string? strength)
    {
        return strength is BindingStrength.Required or BindingStrength.Extensible;
    }

    /// <summary>
    /// Checks if the binding strength is "required", meaning codes MUST come from the value set.
    /// </summary>
    public static bool IsRequiredBinding(string? strength)
    {
        return string.Equals(strength, BindingStrength.Required, StringComparison.OrdinalIgnoreCase);
    }
}
