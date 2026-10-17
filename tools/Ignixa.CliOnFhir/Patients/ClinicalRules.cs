// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// The judgement calls of the patient tools, as FHIRPath over each resource (risk 3 in agent-tool-surface.md: rules must be
/// documented, version-aware and visible in facts). Written and checked against R4 (R4B shares the elements and codes they
/// read); under STU3 or R5 they are reported
/// with a <see cref="UnsupportedVersionCode"/> warning and the status rules are not applied.
/// </summary>
internal static class ClinicalRules
{
    /// <summary>The issue code saying the rules were not written for the session's FHIR version.</summary>
    public const string UnsupportedVersionCode = "domain-rule-unsupported-version";

    /// <summary>
    /// A Condition is an active problem: R4 condition-clinical active, recurrence or relapse, and not refuted or entered in
    /// error by its condition-ver-status.
    /// </summary>
    public const string ActiveCondition =
        "clinicalStatus.coding.where(system = 'http://terminology.hl7.org/CodeSystem/condition-clinical' and (code = 'active' or code = 'recurrence' or code = 'relapse')).exists()"
        + " and verificationStatus.coding.where(system = 'http://terminology.hl7.org/CodeSystem/condition-ver-status' and (code = 'refuted' or code = 'entered-in-error')).empty()";

    /// <summary>A MedicationRequest or MedicationStatement is an active medication: active, and not an order not to give it.</summary>
    public const string ActiveMedication = "status = 'active' and (doNotPerform.empty() or doNotPerform = false)";

    /// <summary>
    /// An Observation counts as a lab or vital sign unless the HL7 observation-category system places it elsewhere: its category
    /// is laboratory or vital-signs, or it has no category, or none of its categories is coded in that system (a local
    /// category such as MIMIC's "Routine Vital Signs" says nothing the rule can read, so it is not taken as "not a lab").
    /// </summary>
    public const string LabCategory =
        "category.empty()"
        + " or category.coding.where(system = 'http://terminology.hl7.org/CodeSystem/observation-category' and (code = 'laboratory' or code = 'vital-signs')).exists()"
        + " or category.coding.where(system = 'http://terminology.hl7.org/CodeSystem/observation-category').empty()";

    /// <summary>The category an Observation is counted under in the map: the code of its first category coding with one.</summary>
    public const string FirstCategoryCode = "category.coding.where(code.exists()).code.first()";

    /// <summary>A Condition with no clinical status: unknown, not inactive (R4 requires one only on a problem-list item).</summary>
    public const string NoClinicalStatus = "clinicalStatus.empty() and verificationStatus.coding.where(system = 'http://terminology.hl7.org/CodeSystem/condition-ver-status' and (code = 'refuted' or code = 'entered-in-error')).empty()";

    /// <summary>Where an Encounter's patient was, one entry per location stay; more than one means a transfer.</summary>
    public const string EncounterLocations = "location";

    /// <summary>An Observation that records a result: not cancelled or entered in error.</summary>
    public const string Recorded = "status != 'cancelled' and status != 'entered-in-error'";

    /// <summary>An Encounter that took or is taking place: not planned, cancelled or entered in error.</summary>
    public const string RecordedEncounter = "status != 'planned' and status != 'cancelled' and status != 'entered-in-error'";

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

    /// <summary>Each type's main code (a CodeableConcept), which <c>patient_context</c> find matches and items display.</summary>
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

    /// <summary>The element whose required binding a status shown by <see cref="StatusCode"/> belongs to; <c>status</c> for the other types.</summary>
    public static readonly IReadOnlyDictionary<string, string> StatusElement = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllergyIntolerance"] = "clinicalStatus",
        ["Condition"] = "clinicalStatus",
    };

    /// <summary>Whether the rules were written for <paramref name="version"/>: R4, and R4B, whose elements and codes they read are R4's.</summary>
    public static bool Verified(FhirVersion version) => version is FhirVersion.R4 or FhirVersion.R4B;

    /// <summary>
    /// Parses and compiles every expression above into FHIRPath's process-wide caches, once, so an unparseable rule fails on
    /// first use rather than part-way through an answer, and later calls only evaluate.
    /// </summary>
    public static void Precompile() => _ = Compiled.Value;

    private static readonly Lazy<bool> Compiled = new(() =>
    {
        IEnumerable<string> all =
        [
            ActiveCondition, ActiveMedication, Recorded, RecordedEncounter, LabCategory, FirstCategoryCode, NoClinicalStatus, EncounterLocations, LabKeyCoding, QuantityValue, ReasonReferences, ReasonCodes, MedicationReference,
            .. PatientName, .. MainDate.Values.SelectMany(expressions => expressions), .. MainCode.Values, .. StatusCode.Values,
        ];
        foreach (string expression in all)
        {
            TypedElementExtensions.Precompile(expression);
        }

        return true;
    });
}
