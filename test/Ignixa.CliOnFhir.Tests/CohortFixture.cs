// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Cohorts;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Resources and calls shared by the patient_cohort tests; every cohort is evaluated as of <see cref="AsOf"/>.</summary>
internal static class CohortFixture
{
    public const string AsOf = "2026-08-17";
    public const string Sct = "http://snomed.info/sct";
    public const string Loinc = "http://loinc.org";
    public const string RxNorm = "http://www.nlm.nih.gov/research/umls/rxnorm";
    public const string Cvx = "http://hl7.org/fhir/sid/cvx";
    public const string Prediabetes = "714628002";
    public const string A1c = "4548-4";
    public const string Ldl = "18262-6";

    public static string Patient(string id, string birthDate, string extra = "") =>
        $$$"""{"resourceType":"Patient","id":"{{{id}}}","birthDate":"{{{birthDate}}}"{{{extra}}}}""";

    public static string Condition(string id, string patient, string code = Prediabetes, string clinical = "active", string extra = "", string system = Sct) =>
        $$$"""{"resourceType":"Condition","id":"{{{id}}}","clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"{{{clinical}}}"}]},"code":{"coding":[{"system":"{{{system}}}","code":"{{{code}}}"}]},"subject":{"reference":"Patient/{{{patient}}}"}{{{extra}}}}""";

    public static string Verification(string code) =>
        $$$""","verificationStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-ver-status","code":"{{{code}}}"}]}""";

    public static string Lab(string id, string patient, string code, string effective, decimal value, string unit = "%", string status = "final") =>
        $$$"""{"resourceType":"Observation","id":"{{{id}}}","status":"{{{status}}}","code":{"coding":[{"system":"{{{Loinc}}}","code":"{{{code}}}"}]},"subject":{"reference":"Patient/{{{patient}}}"},"effectiveDateTime":"{{{effective}}}","valueQuantity":{"value":{{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"unit":"{{{unit}}}","system":"http://unitsofmeasure.org","code":"{{{unit}}}"}}""";

    public static string Order(string id, string patient, string code, string status = "active", string extra = "", string authoredOn = "2025-01-01") =>
        $$$"""{"resourceType":"MedicationRequest","id":"{{{id}}}","status":"{{{status}}}","intent":"order","subject":{"reference":"Patient/{{{patient}}}"},"authoredOn":"{{{authoredOn}}}","medicationCodeableConcept":{"coding":[{"system":"{{{RxNorm}}}","code":"{{{code}}}"}]}{{{extra}}}}""";

    public static string OrderByReference(string id, string patient, string medication, string status = "active") =>
        $$$"""{"resourceType":"MedicationRequest","id":"{{{id}}}","status":"{{{status}}}","intent":"order","subject":{"reference":"Patient/{{{patient}}}"},"authoredOn":"2025-01-01","medicationReference":{"reference":"Medication/{{{medication}}}"}}""";

    public static string Medication(string id, string code) =>
        $$$"""{"resourceType":"Medication","id":"{{{id}}}","code":{"coding":[{"system":"{{{RxNorm}}}","code":"{{{code}}}"}]}}""";

    public static string Immunization(string id, string patient, string cvx, string occurrence, string status = "completed") =>
        $$$"""{"resourceType":"Immunization","id":"{{{id}}}","status":"{{{status}}}","vaccineCode":{"coding":[{"system":"{{{Cvx}}}","code":"{{{cvx}}}"}]},"patient":{"reference":"Patient/{{{patient}}}"},"occurrenceDateTime":"{{{occurrence}}}"}""";

    public static string Lines(params string[] resources) => string.Join('\n', resources);

    public static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static Task<ToolEnvelope<CohortFacts?>> Count(FolderSession session, CohortArguments arguments, int maxRead = CohortEvaluation.MaxResourcesRead) =>
        PatientCohort.RunAsync(session, arguments with { AsOf = arguments.AsOf ?? AsOf }, CancellationToken.None, maxRead);

    public static CohortFacts Facts(ToolEnvelope<CohortFacts?> envelope)
    {
        envelope.Issues.ShouldNotContain(issue => issue.Severity == "error");
        return envelope.Facts.ShouldNotBeNull();
    }

    public static IssueItem Failure(ToolEnvelope<CohortFacts?> envelope)
    {
        envelope.Facts.ShouldBeNull();
        return envelope.Issues.ShouldHaveSingleItem();
    }

    public static (string Criterion, int Remaining)[] Steps(CohortFacts facts) => [.. facts.Steps.Select(step => (step.Criterion, step.Remaining))];
}
