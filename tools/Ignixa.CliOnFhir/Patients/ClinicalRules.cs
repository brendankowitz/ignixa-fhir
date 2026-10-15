// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// The judgement calls of the patient tools, as FHIRPath over each resource (risk 3 in agent-tool-surface.md: rules must be
/// documented, version-aware and visible in facts). Written and checked against R4; under another version they are reported
/// with a <see cref="UnsupportedVersionCode"/> warning and the status rules are not applied.
/// </summary>
internal static class ClinicalRules
{
    /// <summary>The issue code saying the rules were not written for the session's FHIR version.</summary>
    public const string UnsupportedVersionCode = "domain-rule-unsupported-version";

    /// <summary>A Condition is an active problem (R4 condition-clinical: active, recurrence, relapse).</summary>
    public const string ActiveCondition =
        "clinicalStatus.coding.where(system = 'http://terminology.hl7.org/CodeSystem/condition-clinical' and (code = 'active' or code = 'recurrence' or code = 'relapse')).exists()";

    /// <summary>A MedicationRequest or MedicationStatement is an active medication.</summary>
    public const string ActiveMedication = "status = 'active'";

    /// <summary>An Observation counts as a lab or vital sign: its category says so, or it has no category.</summary>
    public const string LabCategory =
        "category.empty() or category.coding.where(system = 'http://terminology.hl7.org/CodeSystem/observation-category' and (code = 'laboratory' or code = 'vital-signs')).exists()";

    /// <summary>An Observation or Encounter that is a record of something that happened: not cancelled or entered in error.</summary>
    public const string Recorded = "status != 'cancelled' and status != 'entered-in-error'";

    /// <summary>The coding a lab series is keyed by: the Observation's first coding with both a system and a code.</summary>
    public const string LabKeyCoding = "code.coding.where(system.exists() and code.exists()).first()";

    /// <summary>A numeric lab value.</summary>
    public const string QuantityValue = "value.ofType(Quantity)";

    /// <summary>Medication references, after Bundle references are resolved.</summary>
    public const string ReasonReferences = "reasonReference.reference";

    /// <summary>The codes a medication gives as its reason.</summary>
    public const string ReasonCodes = "reasonCode";

    /// <summary>The medication a MedicationRequest or MedicationStatement names by reference.</summary>
    public const string MedicationReference = "medication.ofType(Reference).reference";

    /// <summary>The Patient's name used as its display: the first official, else usual, else use-less name.</summary>
    public static readonly IReadOnlyList<string> PatientName =
    [
        "name.where(use = 'official').first()",
        "name.where(use = 'usual').first()",
        "name.where(use.empty()).first()",
    ];

    /// <summary>
    /// Each type's main clinical date: the first of its expressions that yields a date. A type not listed has none and does
    /// not count towards a patient's date span.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> MainDate = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["AllergyIntolerance"] = ["onset.ofType(dateTime)", "onset.ofType(Period).start", "recordedDate"],
        ["CarePlan"] = ["period.start", "created"],
        ["Composition"] = ["date"],
        ["Condition"] = ["onset.ofType(dateTime)", "onset.ofType(Period).start", "recordedDate"],
        ["DiagnosticReport"] = ["effective.ofType(dateTime)", "effective.ofType(Period).start", "issued"],
        ["DocumentReference"] = ["context.period.start", "date"],
        ["Encounter"] = ["period.start"],
        ["ImagingStudy"] = ["started"],
        ["Immunization"] = ["occurrence.ofType(dateTime)", "recorded"],
        ["MedicationAdministration"] = ["effective.ofType(dateTime)", "effective.ofType(Period).start"],
        ["MedicationDispense"] = ["whenHandedOver", "whenPrepared"],
        ["MedicationRequest"] = ["authoredOn"],
        ["MedicationStatement"] = ["effective.ofType(dateTime)", "effective.ofType(Period).start", "dateAsserted"],
        ["Observation"] = ["effective.ofType(dateTime)", "effective.ofType(Period).start", "effective.ofType(instant)", "issued"],
        ["Procedure"] = ["performed.ofType(dateTime)", "performed.ofType(Period).start"],
        ["ServiceRequest"] = ["occurrence.ofType(dateTime)", "occurrence.ofType(Period).start", "authoredOn"],
    };

    /// <summary>Each type's main code (a CodeableConcept), which <c>context_find</c> matches and items display.</summary>
    public static readonly IReadOnlyDictionary<string, string> MainCode = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllergyIntolerance"] = "code",
        ["CarePlan"] = "category",
        ["Composition"] = "type",
        ["Condition"] = "code",
        ["DiagnosticReport"] = "code",
        ["DocumentReference"] = "type",
        ["Encounter"] = "type",
        ["ImagingStudy"] = "procedureCode",
        ["Immunization"] = "vaccineCode",
        ["MedicationAdministration"] = "medication.ofType(CodeableConcept)",
        ["MedicationDispense"] = "medication.ofType(CodeableConcept)",
        ["MedicationRequest"] = "medication.ofType(CodeableConcept)",
        ["MedicationStatement"] = "medication.ofType(CodeableConcept)",
        ["Observation"] = "code",
        ["Procedure"] = "code",
        ["ServiceRequest"] = "code",
    };

    /// <summary>Each type's status code as an item shows it; <c>status</c> for a type not listed.</summary>
    public static readonly IReadOnlyDictionary<string, string> StatusCode = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllergyIntolerance"] = "clinicalStatus.coding.code.first()",
        ["Condition"] = "clinicalStatus.coding.code.first()",
    };

    /// <summary>Whether the rules were written for <paramref name="version"/>.</summary>
    public static bool Verified(FhirVersion version) => version == FhirVersion.R4;

    /// <summary>
    /// Parses and compiles every expression above into FHIRPath's process-wide caches, once, so an unparseable rule fails on
    /// first use rather than part-way through an answer, and later calls only evaluate.
    /// </summary>
    public static void Precompile() => _ = Compiled.Value;

    private static readonly Lazy<bool> Compiled = new(() =>
    {
        IEnumerable<string> all =
        [
            ActiveCondition, ActiveMedication, Recorded, LabCategory, LabKeyCoding, QuantityValue, ReasonReferences, ReasonCodes, MedicationReference,
            .. PatientName, .. MainDate.Values.SelectMany(expressions => expressions), .. MainCode.Values, .. StatusCode.Values,
        ];
        foreach (string expression in all)
        {
            TypedElementExtensions.Precompile(expression);
        }

        return true;
    });
}
