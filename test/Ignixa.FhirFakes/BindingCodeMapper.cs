// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.FhirFakes.Scenarios.Codes;

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

    // Cached code arrays for value sets - loaded lazily from static properties
    private static FhirCode[]? _allergenCodes;
    private static FhirCode[]? _immunizationCodes;
    private static FhirCode[]? _labObservationCodes;
    private static FhirCode[]? _procedureCodes;
    private static FhirCode[]? _vitalSignCodes;
    private static FhirCode[]? _diagnosticReportCodes;
    private static FhirCode[]? _medicationCodes;
    private static FhirCode[]? _conditionCodes;
    private static FhirCode[]? _encounterTypeCodes;

    /// <summary>
    /// Attempts to get predefined codes for a value set URI.
    /// </summary>
    /// <param name="valueSetUri">The canonical URL of the value set from the binding.</param>
    /// <param name="codes">The array of predefined codes if found.</param>
    /// <returns>True if codes were found for this value set, false otherwise.</returns>
    public static bool TryGetCodesForValueSet(string? valueSetUri, out FhirCode[] codes)
    {
        if (string.IsNullOrEmpty(valueSetUri))
        {
            codes = [];
            return false;
        }

        codes = GetCodesForValueSetInternal(valueSetUri);
        return codes.Length > 0;
    }

    /// <summary>
    /// Gets codes for a value set URI, returning an empty array if no mapping exists.
    /// </summary>
    private static FhirCode[] GetCodesForValueSetInternal(string valueSetUri)
    {
        // Normalize the URI (remove version suffixes like |4.0.1)
        var normalizedUri = NormalizeValueSetUri(valueSetUri);

        return normalizedUri switch
        {
            // Administrative Gender - http://hl7.org/fhir/ValueSet/administrative-gender
            "http://hl7.org/fhir/ValueSet/administrative-gender" =>
            [
                new FhirCode("http://hl7.org/fhir/administrative-gender", "male", "Male"),
                new FhirCode("http://hl7.org/fhir/administrative-gender", "female", "Female"),
                new FhirCode("http://hl7.org/fhir/administrative-gender", "other", "Other"),
                new FhirCode("http://hl7.org/fhir/administrative-gender", "unknown", "Unknown")
            ],

            // Name Use - http://hl7.org/fhir/ValueSet/name-use
            "http://hl7.org/fhir/ValueSet/name-use" =>
            [
                new FhirCode("http://hl7.org/fhir/name-use", "usual", "Usual"),
                new FhirCode("http://hl7.org/fhir/name-use", "official", "Official"),
                new FhirCode("http://hl7.org/fhir/name-use", "temp", "Temp"),
                new FhirCode("http://hl7.org/fhir/name-use", "nickname", "Nickname"),
                new FhirCode("http://hl7.org/fhir/name-use", "anonymous", "Anonymous"),
                new FhirCode("http://hl7.org/fhir/name-use", "old", "Old"),
                new FhirCode("http://hl7.org/fhir/name-use", "maiden", "Maiden")
            ],

            // Address Use - http://hl7.org/fhir/ValueSet/address-use
            "http://hl7.org/fhir/ValueSet/address-use" =>
            [
                new FhirCode("http://hl7.org/fhir/address-use", "home", "Home"),
                new FhirCode("http://hl7.org/fhir/address-use", "work", "Work"),
                new FhirCode("http://hl7.org/fhir/address-use", "temp", "Temporary"),
                new FhirCode("http://hl7.org/fhir/address-use", "old", "Old / Incorrect"),
                new FhirCode("http://hl7.org/fhir/address-use", "billing", "Billing")
            ],

            // Address Type - http://hl7.org/fhir/ValueSet/address-type
            "http://hl7.org/fhir/ValueSet/address-type" =>
            [
                new FhirCode("http://hl7.org/fhir/address-type", "postal", "Postal"),
                new FhirCode("http://hl7.org/fhir/address-type", "physical", "Physical"),
                new FhirCode("http://hl7.org/fhir/address-type", "both", "Postal & Physical")
            ],

            // Contact Point System - http://hl7.org/fhir/ValueSet/contact-point-system
            "http://hl7.org/fhir/ValueSet/contact-point-system" =>
            [
                new FhirCode("http://hl7.org/fhir/contact-point-system", "phone", "Phone"),
                new FhirCode("http://hl7.org/fhir/contact-point-system", "fax", "Fax"),
                new FhirCode("http://hl7.org/fhir/contact-point-system", "email", "Email"),
                new FhirCode("http://hl7.org/fhir/contact-point-system", "pager", "Pager"),
                new FhirCode("http://hl7.org/fhir/contact-point-system", "url", "URL"),
                new FhirCode("http://hl7.org/fhir/contact-point-system", "sms", "SMS"),
                new FhirCode("http://hl7.org/fhir/contact-point-system", "other", "Other")
            ],

            // Contact Point Use - http://hl7.org/fhir/ValueSet/contact-point-use
            "http://hl7.org/fhir/ValueSet/contact-point-use" =>
            [
                new FhirCode("http://hl7.org/fhir/contact-point-use", "home", "Home"),
                new FhirCode("http://hl7.org/fhir/contact-point-use", "work", "Work"),
                new FhirCode("http://hl7.org/fhir/contact-point-use", "temp", "Temp"),
                new FhirCode("http://hl7.org/fhir/contact-point-use", "old", "Old"),
                new FhirCode("http://hl7.org/fhir/contact-point-use", "mobile", "Mobile")
            ],

            // Identifier Use - http://hl7.org/fhir/ValueSet/identifier-use
            "http://hl7.org/fhir/ValueSet/identifier-use" =>
            [
                new FhirCode("http://hl7.org/fhir/identifier-use", "usual", "Usual"),
                new FhirCode("http://hl7.org/fhir/identifier-use", "official", "Official"),
                new FhirCode("http://hl7.org/fhir/identifier-use", "temp", "Temp"),
                new FhirCode("http://hl7.org/fhir/identifier-use", "secondary", "Secondary"),
                new FhirCode("http://hl7.org/fhir/identifier-use", "old", "Old")
            ],

            // Observation Status - http://hl7.org/fhir/ValueSet/observation-status
            "http://hl7.org/fhir/ValueSet/observation-status" =>
            [
                new FhirCode("http://hl7.org/fhir/observation-status", "registered", "Registered"),
                new FhirCode("http://hl7.org/fhir/observation-status", "preliminary", "Preliminary"),
                new FhirCode("http://hl7.org/fhir/observation-status", "final", "Final"),
                new FhirCode("http://hl7.org/fhir/observation-status", "amended", "Amended"),
                new FhirCode("http://hl7.org/fhir/observation-status", "corrected", "Corrected"),
                new FhirCode("http://hl7.org/fhir/observation-status", "cancelled", "Cancelled"),
                new FhirCode("http://hl7.org/fhir/observation-status", "entered-in-error", "Entered in Error"),
                new FhirCode("http://hl7.org/fhir/observation-status", "unknown", "Unknown")
            ],

            // Condition Clinical Status - http://hl7.org/fhir/ValueSet/condition-clinical
            "http://hl7.org/fhir/ValueSet/condition-clinical" =>
            [
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-clinical", "active", "Active"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-clinical", "recurrence", "Recurrence"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-clinical", "relapse", "Relapse"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-clinical", "inactive", "Inactive"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-clinical", "remission", "Remission"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-clinical", "resolved", "Resolved")
            ],

            // Condition Verification Status - http://hl7.org/fhir/ValueSet/condition-ver-status
            "http://hl7.org/fhir/ValueSet/condition-ver-status" =>
            [
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-ver-status", "unconfirmed", "Unconfirmed"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-ver-status", "provisional", "Provisional"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-ver-status", "differential", "Differential"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-ver-status", "confirmed", "Confirmed"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-ver-status", "refuted", "Refuted"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/condition-ver-status", "entered-in-error", "Entered in Error")
            ],

            // Encounter Status - http://hl7.org/fhir/ValueSet/encounter-status
            "http://hl7.org/fhir/ValueSet/encounter-status" =>
            [
                new FhirCode("http://hl7.org/fhir/encounter-status", "planned", "Planned"),
                new FhirCode("http://hl7.org/fhir/encounter-status", "arrived", "Arrived"),
                new FhirCode("http://hl7.org/fhir/encounter-status", "triaged", "Triaged"),
                new FhirCode("http://hl7.org/fhir/encounter-status", "in-progress", "In Progress"),
                new FhirCode("http://hl7.org/fhir/encounter-status", "onleave", "On Leave"),
                new FhirCode("http://hl7.org/fhir/encounter-status", "finished", "Finished"),
                new FhirCode("http://hl7.org/fhir/encounter-status", "cancelled", "Cancelled"),
                new FhirCode("http://hl7.org/fhir/encounter-status", "entered-in-error", "Entered in Error"),
                new FhirCode("http://hl7.org/fhir/encounter-status", "unknown", "Unknown")
            ],

            // Medication Request Status - http://hl7.org/fhir/ValueSet/medicationrequest-status
            "http://hl7.org/fhir/ValueSet/medicationrequest-status" =>
            [
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "active", "Active"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "on-hold", "On Hold"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "cancelled", "Cancelled"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "completed", "Completed"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "entered-in-error", "Entered in Error"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "stopped", "Stopped"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "draft", "Draft"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "unknown", "Unknown")
            ],

            // Medication Request Intent - http://hl7.org/fhir/ValueSet/medicationrequest-intent
            "http://hl7.org/fhir/ValueSet/medicationrequest-intent" =>
            [
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "proposal", "Proposal"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "plan", "Plan"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "order", "Order"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "original-order", "Original Order"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "reflex-order", "Reflex Order"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "filler-order", "Filler Order"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "instance-order", "Instance Order"),
                new FhirCode("http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "option", "Option")
            ],

            // Procedure Status - http://hl7.org/fhir/ValueSet/event-status
            "http://hl7.org/fhir/ValueSet/event-status" =>
            [
                new FhirCode("http://hl7.org/fhir/event-status", "preparation", "Preparation"),
                new FhirCode("http://hl7.org/fhir/event-status", "in-progress", "In Progress"),
                new FhirCode("http://hl7.org/fhir/event-status", "not-done", "Not Done"),
                new FhirCode("http://hl7.org/fhir/event-status", "on-hold", "On Hold"),
                new FhirCode("http://hl7.org/fhir/event-status", "stopped", "Stopped"),
                new FhirCode("http://hl7.org/fhir/event-status", "completed", "Completed"),
                new FhirCode("http://hl7.org/fhir/event-status", "entered-in-error", "Entered in Error"),
                new FhirCode("http://hl7.org/fhir/event-status", "unknown", "Unknown")
            ],

            // AllergyIntolerance Clinical Status - http://hl7.org/fhir/ValueSet/allergyintolerance-clinical
            "http://hl7.org/fhir/ValueSet/allergyintolerance-clinical" =>
            [
                new FhirCode("http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical", "active", "Active"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical", "inactive", "Inactive"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical", "resolved", "Resolved")
            ],

            // AllergyIntolerance Verification Status - http://hl7.org/fhir/ValueSet/allergyintolerance-verification
            "http://hl7.org/fhir/ValueSet/allergyintolerance-verification" =>
            [
                new FhirCode("http://terminology.hl7.org/CodeSystem/allergyintolerance-verification", "unconfirmed", "Unconfirmed"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/allergyintolerance-verification", "confirmed", "Confirmed"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/allergyintolerance-verification", "refuted", "Refuted"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/allergyintolerance-verification", "entered-in-error", "Entered in Error")
            ],

            // AllergyIntolerance Type - http://hl7.org/fhir/ValueSet/allergy-intolerance-type
            "http://hl7.org/fhir/ValueSet/allergy-intolerance-type" =>
            [
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-type", "allergy", "Allergy"),
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-type", "intolerance", "Intolerance")
            ],

            // AllergyIntolerance Category - http://hl7.org/fhir/ValueSet/allergy-intolerance-category
            "http://hl7.org/fhir/ValueSet/allergy-intolerance-category" =>
            [
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-category", "food", "Food"),
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-category", "medication", "Medication"),
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-category", "environment", "Environment"),
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-category", "biologic", "Biologic")
            ],

            // AllergyIntolerance Criticality - http://hl7.org/fhir/ValueSet/allergy-intolerance-criticality
            "http://hl7.org/fhir/ValueSet/allergy-intolerance-criticality" =>
            [
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-criticality", "low", "Low Risk"),
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-criticality", "high", "High Risk"),
                new FhirCode("http://hl7.org/fhir/allergy-intolerance-criticality", "unable-to-assess", "Unable to Assess Risk")
            ],

            // Immunization Status - http://hl7.org/fhir/ValueSet/immunization-status
            "http://hl7.org/fhir/ValueSet/immunization-status" =>
            [
                new FhirCode("http://hl7.org/fhir/event-status", "completed", "Completed"),
                new FhirCode("http://hl7.org/fhir/event-status", "entered-in-error", "Entered in Error"),
                new FhirCode("http://hl7.org/fhir/event-status", "not-done", "Not Done")
            ],

            // Diagnostic Report Status - http://hl7.org/fhir/ValueSet/diagnostic-report-status
            "http://hl7.org/fhir/ValueSet/diagnostic-report-status" =>
            [
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "registered", "Registered"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "partial", "Partial"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "preliminary", "Preliminary"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "final", "Final"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "amended", "Amended"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "corrected", "Corrected"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "appended", "Appended"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "cancelled", "Cancelled"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "entered-in-error", "Entered in Error"),
                new FhirCode("http://hl7.org/fhir/diagnostic-report-status", "unknown", "Unknown")
            ],

            // Marital Status - http://hl7.org/fhir/ValueSet/marital-status
            "http://hl7.org/fhir/ValueSet/marital-status" =>
            [
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "A", "Annulled"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "D", "Divorced"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "I", "Interlocutory"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "L", "Legally Separated"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "M", "Married"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "P", "Polygamous"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "S", "Never Married"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "T", "Domestic Partner"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "U", "Unmarried"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "W", "Widowed"),
                new FhirCode("http://terminology.hl7.org/CodeSystem/v3-NullFlavor", "UNK", "Unknown")
            ],

            // Link Type (Patient) - http://hl7.org/fhir/ValueSet/link-type
            "http://hl7.org/fhir/ValueSet/link-type" =>
            [
                new FhirCode("http://hl7.org/fhir/link-type", "replaced-by", "Replaced-by"),
                new FhirCode("http://hl7.org/fhir/link-type", "replaces", "Replaces"),
                new FhirCode("http://hl7.org/fhir/link-type", "refer", "Refer"),
                new FhirCode("http://hl7.org/fhir/link-type", "seealso", "See also")
            ],

            // ===== CLINICAL TERMINOLOGY VALUE SETS =====

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
    public static FhirCode[] GetAllAllergenCodes()
    {
        return _allergenCodes ??= GetCodesFromStaticProperties(typeof(Allergens));
    }

    /// <summary>
    /// Gets all immunization (vaccine) codes from the Immunizations class.
    /// </summary>
    public static FhirCode[] GetAllImmunizationCodes()
    {
        return _immunizationCodes ??= GetCodesFromStaticProperties(typeof(Immunizations));
    }

    /// <summary>
    /// Gets all laboratory observation codes from the LabObservations class.
    /// </summary>
    public static FhirCode[] GetAllLabObservationCodes()
    {
        return _labObservationCodes ??= GetCodesFromStaticProperties(typeof(LabObservations));
    }

    /// <summary>
    /// Gets all procedure codes from the Procedures class.
    /// </summary>
    public static FhirCode[] GetAllProcedureCodes()
    {
        return _procedureCodes ??= GetCodesFromStaticProperties(typeof(Procedures));
    }

    /// <summary>
    /// Gets all vital sign observation codes from the VitalSigns class.
    /// </summary>
    public static FhirCode[] GetAllVitalSignCodes()
    {
        return _vitalSignCodes ??= GetCodesFromStaticProperties(typeof(VitalSigns));
    }

    /// <summary>
    /// Gets all diagnostic report type codes from the DiagnosticReports class.
    /// </summary>
    public static FhirCode[] GetAllDiagnosticReportCodes()
    {
        return _diagnosticReportCodes ??= GetCodesFromStaticProperties(typeof(DiagnosticReports));
    }

    /// <summary>
    /// Gets all medication codes from the FhirCode.Medications nested class.
    /// </summary>
    public static FhirCode[] GetAllMedicationCodes()
    {
        return _medicationCodes ??= GetCodesFromStaticFields(typeof(FhirCode.Medications));
    }

    /// <summary>
    /// Gets all condition codes from the FhirCode.Conditions nested class.
    /// </summary>
    public static FhirCode[] GetAllConditionCodes()
    {
        return _conditionCodes ??= GetCodesFromStaticFields(typeof(FhirCode.Conditions));
    }

    /// <summary>
    /// Gets all encounter type codes from the FhirCode.EncounterTypes nested class.
    /// </summary>
    public static FhirCode[] GetAllEncounterTypeCodes()
    {
        return _encounterTypeCodes ??= GetCodesFromStaticFields(typeof(FhirCode.EncounterTypes));
    }

    /// <summary>
    /// Gets combined lab observation and vital sign codes.
    /// </summary>
    private static FhirCode[] GetAllLabAndVitalSignCodes()
    {
        var labCodes = GetAllLabObservationCodes();
        var vitalCodes = GetAllVitalSignCodes();
        return [.. labCodes, .. vitalCodes];
    }

    /// <summary>
    /// Extracts FhirCode values from all public static properties of a type.
    /// </summary>
    private static FhirCode[] GetCodesFromStaticProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(FhirCode))
            .Select(p => (FhirCode)p.GetValue(null)!)
            .ToArray();
    }

    /// <summary>
    /// Extracts FhirCode values from all public static fields of a type.
    /// </summary>
    private static FhirCode[] GetCodesFromStaticFields(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(FhirCode))
            .Select(f => (FhirCode)f.GetValue(null)!)
            .ToArray();
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
