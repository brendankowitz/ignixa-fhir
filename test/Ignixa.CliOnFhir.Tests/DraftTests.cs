// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Drafting;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DraftFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Drafts and their checks (Task 12, section B): new behaviour, no draft verb exists before it.</summary>
public class DraftTests
{
    [Fact]
    public async Task GivenAPotassiumOrder_WhenDrafted_ThenItIsAValidTaggedMedicationRequestDatedAsOf()
    {
        using TestFolder folder = Clinic();

        DraftFacts facts = Facts(await RunAsync(folder, new DraftRequest("MedicationRequest", PotassiumOrder)));

        facts.Verdict.ShouldBe(DraftFacts.Valid, string.Join("; ", facts.Checks.Select(check => $"{check.Check} {check.Element} {check.Outcome}")));
        JsonObject resource = facts.Resource;
        Text(resource["resourceType"]).ShouldBe("MedicationRequest");
        Text(resource["id"]).ShouldMatch("^draft-[0-9a-f]{32}$");
        Text(resource["status"]).ShouldBe("draft");
        Text(resource["intent"]).ShouldBe("order");
        Text(resource["authoredOn"]).ShouldBe(Instant);
        Text(resource["subject"]!["reference"]).ShouldBe("Patient/p1");
        Text(resource["encounter"]!["reference"]).ShouldBe("Encounter/e1");
        Text(resource["meta"]!["tag"]![0]!["system"]).ShouldBe(DraftBuilder.TagSystem);
        Text(resource["meta"]!["tag"]![0]!["code"]).ShouldBe("draft");
        resource["meta"]!["profile"].ShouldBeNull();
        JsonNode dosage = resource["dosageInstruction"]![0]!;
        ((int)dosage["timing"]!["repeat"]!["frequency"]!).ShouldBe(2);
        Text(dosage["doseAndRate"]![0]!["doseQuantity"]!["code"]).ShouldBe("meq");
        Text(dosage["route"]!["coding"]![0]!["code"]).ShouldBe("26643006");
        Check(facts, "terminology", "MedicationRequest.medicationCodeableConcept.coding[0]").Outcome.ShouldBe(DraftCheck.Pass);
        Check(facts, "reference", "MedicationRequest.encounter").Outcome.ShouldBe(DraftCheck.Pass);
        facts.AsOf.ShouldBe(new AsOf(DateTimeOffset.Parse(Instant, System.Globalization.CultureInfo.InvariantCulture), AsOfSources.SessionOption));
    }

    [Fact]
    public async Task GivenAMagnesiumLevelOrder_WhenDrafted_ThenItIsAValidServiceRequestWithItsReasons()
    {
        using TestFolder folder = Clinic();

        DraftFacts facts = Facts(await RunAsync(folder, new DraftRequest("ServiceRequest", MagnesiumOrder)));

        facts.Verdict.ShouldBe(DraftFacts.Valid, string.Join("; ", facts.Checks.Select(check => $"{check.Check} {check.Element} {check.Outcome}")));
        Text(facts.Resource["status"]).ShouldBe("draft");
        Text(facts.Resource["priority"]).ShouldBe("routine");
        Text(facts.Resource["authoredOn"]).ShouldBe(Instant);
        facts.Resource["occurrenceDateTime"].ShouldBeNull();
        Text(facts.Resource["reasonReference"]![0]!["reference"]).ShouldBe("Condition/c1");
        Check(facts, "reference", "ServiceRequest.reasonReference[0]").Outcome.ShouldBe(DraftCheck.Pass);
        Check(facts, "terminology", "ServiceRequest.reasonCode[0].coding[0]").Outcome.ShouldBe(DraftCheck.Pass);
    }

    [Fact]
    public async Task GivenAnObservationWithAQuantity_WhenDrafted_ThenItIsValidAndEffectiveAndIssuedAreAsOf()
    {
        using TestFolder folder = Clinic();

        DraftFacts facts = Facts(await RunAsync(folder, new DraftRequest("Observation", PotassiumResult)));

        facts.Verdict.ShouldBe(DraftFacts.Valid, string.Join("; ", facts.Checks.Select(check => $"{check.Check} {check.Element} {check.Outcome}")));
        Text(facts.Resource["status"]).ShouldBe("preliminary");
        Text(facts.Resource["effectiveDateTime"]).ShouldBe(Instant);
        Text(facts.Resource["issued"]).ShouldBe(Instant);
        ((decimal)facts.Resource["valueQuantity"]!["value"]!).ShouldBe(3.1m);
        Check(facts, "terminology", "Observation.valueQuantity").Outcome.ShouldBe(DraftCheck.Pass);
    }

    [Fact]
    public async Task GivenDatesInTheInput_WhenDrafted_ThenTheyAreKeptInsteadOfAsOf()
    {
        using TestFolder folder = Clinic();
        string input = PotassiumResult.Replace("\"patient\":\"Patient/p1\"", "\"patient\":\"Patient/p1\",\"effective\":\"2024-04-30\",\"issued\":\"2024-04-30T08:00:00+02:00\"", StringComparison.Ordinal);

        DraftFacts facts = Facts(await RunAsync(folder, new DraftRequest("Observation", input)));

        Text(facts.Resource["effectiveDateTime"]).ShouldBe("2024-04-30");
        Text(facts.Resource["issued"]).ShouldBe("2024-04-30T08:00:00+02:00");
    }

    [Fact]
    public async Task GivenNoAsOfOption_WhenDrafted_ThenAuthoredOnIsTheSessionClock()
    {
        using TestFolder folder = Clinic();
        var clock = new ManualTimeProvider { Now = new DateTimeOffset(2025, 3, 31, 23, 59, 58, 123, TimeSpan.Zero).AddTicks(4567) };
        FolderSession session = Open(folder, clock: clock);

        DraftFacts facts = Facts(await RunAsync(session, new DraftRequest("MedicationRequest", PotassiumOrder), asOf: null));

        Text(facts.Resource["authoredOn"]).ShouldBe("2025-03-31T23:59:58.123Z");
        facts.AsOf.Source.ShouldBe(AsOfSources.Clock);
    }

    [Fact]
    public async Task GivenAPatientNotInTheFolder_WhenDrafted_ThenPatientNotFoundIsAnErrorAndTheDraftIsInvalid()
    {
        using TestFolder folder = Clinic();
        string input = PotassiumOrder.Replace("Patient/p1", "Patient/nobody", StringComparison.Ordinal).Replace(",\"encounter\":\"Encounter/e1\"", string.Empty, StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("MedicationRequest", input));

        DraftFacts facts = Facts(envelope);
        facts.Verdict.ShouldBe(DraftFacts.Invalid);
        Check(facts, "reference", "MedicationRequest.subject").Details.ShouldBe(new ReferenceCheckDetails("Patient/nobody", DraftReferences.NotFound));
        envelope.Issues.Single(issue => issue.Code == "patient-not-found").Severity.ShouldBe("error");
    }

    [Fact]
    public async Task GivenAnotherPatientsEncounterOrCondition_WhenDrafted_ThenTheReferenceFails()
    {
        using TestFolder folder = Clinic();
        string input = MagnesiumOrder.Replace("Condition/c1", "Condition/c2", StringComparison.Ordinal).Replace("\"patient\":\"p1\"", "\"patient\":\"p1\",\"encounter\":\"e2\"", StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("ServiceRequest", input));

        DraftFacts facts = Facts(envelope);
        facts.Verdict.ShouldBe(DraftFacts.Invalid);
        ((ReferenceCheckDetails)Check(facts, "reference", "ServiceRequest.encounter").Details).Result.ShouldBe(DraftReferences.OtherPatient);
        ((ReferenceCheckDetails)Check(facts, "reference", "ServiceRequest.reasonReference[0]").Details).Result.ShouldBe(DraftReferences.OtherPatient);
        envelope.Issues.Count(issue => issue.Code == "reference-other-patient").ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnEncounterNotInTheFolder_WhenDrafted_ThenEncounterNotFoundIsAnError()
    {
        using TestFolder folder = Clinic();
        string input = PotassiumOrder.Replace("Encounter/e1", "Encounter/e9", StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("MedicationRequest", input));

        Facts(envelope).Verdict.ShouldBe(DraftFacts.Invalid);
        envelope.Issues.Single(issue => issue.Code == "encounter-not-found").Severity.ShouldBe("error");
    }

    [Fact]
    public async Task GivenAWrongDisplayOnTheMedication_WhenDrafted_ThenTheTerminologyCheckFailsAndTheDraftIsInvalid()
    {
        using TestFolder folder = Clinic();
        const string given = "potassium chloride 10 MEQ tablet";
        string input = PotassiumOrder.Replace(PotassiumTabletDisplay, given, StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("MedicationRequest", input));

        DraftFacts facts = Facts(envelope);
        DraftCheck check = Check(facts, "terminology", "MedicationRequest.medicationCodeableConcept.coding[0]");
        check.Outcome.ShouldBe(DraftCheck.Fail);
        var details = (TerminologyCheckDetails)check.Details;
        details.ExpectedDisplay.ShouldBe(PotassiumTabletDisplay);
        details.DisplayMatches.ShouldBe(false);
        details.Result.ShouldBe("invalid");
        details.Reason.ShouldBe("display-mismatch");
        facts.Verdict.ShouldBe(DraftFacts.Invalid);
        IssueItem error = envelope.Issues.Single(issue => issue.Code == "display-mismatch");
        error.Severity.ShouldBe("error");
        error.Diagnostics.ShouldContain("expectedDisplay");
        error.Diagnostics.ShouldContain("MedicationRequest.medicationCodeableConcept.coding[0]");
        error.Diagnostics.ShouldNotContain(given);
        error.Diagnostics.ShouldNotContain(PotassiumTabletDisplay);
    }

    [Theory]
    [InlineData("ServiceRequest", "Magnesium", "ServiceRequest.code.coding[0]")]
    [InlineData("Observation", "Potassium", "Observation.code.coding[0]")]
    public async Task GivenAWrongDisplayOnWhatTheDraftIs_WhenDrafted_ThenTheDraftIsInvalid(string type, string display, string element)
    {
        using TestFolder folder = Clinic();
        string input = type == "ServiceRequest"
            ? MagnesiumOrder.Replace(MagnesiumDisplay, display, StringComparison.Ordinal)
            : PotassiumResult.Replace("\"code\":\"2823-3\"", $"\"code\":\"2823-3\",\"display\":\"{display}\"", StringComparison.Ordinal);

        DraftFacts facts = Facts(await RunAsync(folder, new DraftRequest(type, input)));

        Check(facts, "terminology", element).Outcome.ShouldBe(DraftCheck.Fail);
        facts.Verdict.ShouldBe(DraftFacts.Invalid);
    }

    [Fact]
    public async Task GivenAWrongDisplayOnASecondaryCoding_WhenDrafted_ThenItIsAWarningAndTheDraftStaysValid()
    {
        using TestFolder folder = Clinic();
        string input = PotassiumOrder.Replace("\"Oral route\"", "\"By mouth\"", StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("MedicationRequest", input));

        DraftFacts facts = Facts(envelope);
        DraftCheck check = Check(facts, "terminology", "MedicationRequest.dosageInstruction[0].route.coding[0]");
        check.Outcome.ShouldBe(DraftCheck.Warning);
        ((TerminologyCheckDetails)check.Details).ExpectedDisplay.ShouldBe("Oral route");
        facts.Verdict.ShouldBe(DraftFacts.Valid);
        envelope.Issues.Single(issue => issue.Code == "display-mismatch").Severity.ShouldBe("warning");
    }

    [Fact]
    public async Task GivenACodeAFragmentOfItsSystemLacks_WhenDrafted_ThenItIsUnknownAndAnIssueSaysWhy()
    {
        using TestFolder folder = Clinic();
        string input = MagnesiumOrder.Replace($"\"code\":\"{Magnesium}\"", "\"code\":\"2345-7\"", StringComparison.Ordinal).Replace($",\"display\":\"{MagnesiumDisplay}\"", string.Empty, StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("ServiceRequest", input));

        DraftFacts facts = Facts(envelope);
        Check(facts, "terminology", "ServiceRequest.code.coding[0]").Outcome.ShouldBe(DraftCheck.Unknown);
        facts.Verdict.ShouldBe(DraftFacts.Unknown);
        envelope.Issues.Single(issue => issue.Code == "code-not-in-loaded-content").Diagnostics.ShouldContain("ServiceRequest.code.coding[0]");
    }

    [Fact]
    public async Task GivenUnknownTerminologyChecks_WhenDrafted_ThenEachIsExplainedByAnIssueNamingItsElement()
    {
        using TestFolder folder = Clinic();
        JsonObject input = JsonNode.Parse(PotassiumOrder)!.AsObject();
        input["medication"] = JsonNode.Parse($$"""{"system":"{{RxNorm}}","code":"{{PotassiumTablet}}"}""");
        input["route"] = JsonNode.Parse("""{"system":"http://example.org/routes","code":"po"}""");
        input["dose"] = JsonNode.Parse("""{"value":20,"unit":"mEq","unitSystem":"http://example.org/units","code":"meq"}""");

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("MedicationRequest", input.ToJsonString()));

        List<DraftCheck> unknown = [.. Facts(envelope).Checks.Where(check => check.Check == "terminology" && check.Outcome == DraftCheck.Unknown)];
        unknown.Count.ShouldBe(2);
        unknown.ShouldAllBe(check => envelope.Issues.Any(issue => issue.Severity != "information" && issue.Diagnostics.Contains(check.Element, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GivenACodeTheSystemDoesNotHave_WhenDrafted_ThenTheTerminologyCheckFails()
    {
        using TestFolder folder = Clinic();
        string input = PotassiumOrder.Replace($"\"code\":\"{PotassiumTablet}\"", "\"code\":\"999999\"", StringComparison.Ordinal);

        DraftFacts facts = Facts(await RunAsync(folder, new DraftRequest("MedicationRequest", input)));

        Check(facts, "terminology", "MedicationRequest.medicationCodeableConcept.coding[0]").Outcome.ShouldBe(DraftCheck.Fail);
        facts.Verdict.ShouldBe(DraftFacts.Invalid);
    }

    [Fact]
    public async Task GivenASystemThatIsNotLoaded_WhenDrafted_ThenSystemNotLoadedIsAWarningAndTheVerdictIsUnknown()
    {
        using TestFolder folder = Clinic();
        string input = MagnesiumOrder.Replace(Loinc, "http://example.org/codes/labs", StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("ServiceRequest", input));

        DraftFacts facts = Facts(envelope);
        DraftCheck check = Check(facts, "terminology", "ServiceRequest.code.coding[0]");
        check.Outcome.ShouldBe(DraftCheck.Unknown);
        ((TerminologyCheckDetails)check.Details).Reason.ShouldBe("system-not-loaded");
        facts.Verdict.ShouldBe(DraftFacts.Unknown);
        envelope.Issues.Single(issue => issue.Code == DraftTerminology.SystemNotLoadedCode).Severity.ShouldBe("warning");
    }

    [Fact]
    public async Task GivenAStatusOutsideItsRequiredBinding_WhenDrafted_ThenValidationFails()
    {
        using TestFolder folder = Clinic();
        string input = PotassiumOrder.Replace("\"patient\":\"Patient/p1\"", "\"patient\":\"Patient/p1\",\"status\":\"pending\"", StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("MedicationRequest", input));

        DraftFacts facts = Facts(envelope);
        DraftCheck validation = Check(facts, "validation", "MedicationRequest");
        validation.Outcome.ShouldBe(DraftCheck.Fail);
        ((ValidationCheckDetails)validation.Details).Issues.ShouldContain(issue => issue.Rule == "not-in-value-set" && issue.Path == "MedicationRequest.status");
        facts.Verdict.ShouldBe(DraftFacts.Invalid);
        envelope.Issues.Single(issue => issue.Code == "draft-invalid").Severity.ShouldBe("error");
    }

    [Fact]
    public async Task GivenNoTerminologyForTheRequiredBindings_WhenDrafted_ThenTheVerdictIsUnknownNotValid()
    {
        using TestFolder folder = new TestFolder().Write("clinic.ndjson", """{"resourceType":"Patient","id":"p1"}""");
        string input = PotassiumOrder.Replace(",\"encounter\":\"Encounter/e1\"", string.Empty, StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(folder, new DraftRequest("MedicationRequest", input));

        DraftFacts facts = Facts(envelope);
        Check(facts, "validation", "MedicationRequest").Outcome.ShouldBe(DraftCheck.Unknown);
        facts.Verdict.ShouldBe(DraftFacts.Unknown);
        envelope.Issues.ShouldContain(issue => issue.Code == "draft-not-fully-validated");
    }

    [Fact]
    public async Task GivenAProfileTheDraftViolates_WhenDrafted_ThenItClaimsTheProfileAndValidationFails()
    {
        using TestFolder folder = ClinicWithProfile();
        JsonObject withoutCategory = JsonNode.Parse(PotassiumResult)!.AsObject();
        withoutCategory.Remove("category");
        string input = withoutCategory.ToJsonString();

        DraftFacts facts = Facts(await RunAsync(folder, new DraftRequest("Observation", input, Profile: CategoryRequired)));

        Text(facts.Resource["meta"]!["profile"]![0]).ShouldBe(CategoryRequired);
        DraftCheck validation = Check(facts, "validation", "Observation");
        validation.Outcome.ShouldBe(DraftCheck.Fail);
        var details = (ValidationCheckDetails)validation.Details;
        details.Profile.ShouldBe(CategoryRequired + "|1.0.0");
        details.Issues.ShouldContain(issue => issue.Profile == CategoryRequired + "|1.0.0" && issue.Rule == "cardinality-violation" && issue.Path == "Observation.category");
        facts.Verdict.ShouldBe(DraftFacts.Invalid);
    }

    [Fact]
    public async Task GivenAProfileTheDraftMeets_WhenDrafted_ThenItIsValid()
    {
        using TestFolder folder = ClinicWithProfile();

        DraftFacts facts = Facts(await RunAsync(folder, new DraftRequest("Observation", PotassiumResult, Profile: CategoryRequired)));

        facts.Verdict.ShouldBe(DraftFacts.Valid, string.Join("; ", facts.Checks.Select(check => $"{check.Check} {check.Element} {check.Outcome}")));
    }

    [Fact]
    public async Task GivenAProfileThatIsNotLoaded_WhenDrafted_ThenItIsARequestError()
    {
        using TestFolder folder = Clinic();

        IssueItem error = Error(await RunAsync(folder, new DraftRequest("Observation", PotassiumResult, Profile: "http://example.org/fhir/StructureDefinition/missing")));

        error.Code.ShouldBe("profile-not-loaded");
    }

    [Fact]
    public async Task GivenTheSameInputTwice_WhenDrafted_ThenTheIdIsTheSameWhateverItsLayout()
    {
        using TestFolder folder = Clinic();
        JsonObject reordered = JsonNode.Parse(PotassiumOrder)!.AsObject();
        var shuffled = new JsonObject();
        foreach (string key in reordered.Select(pair => pair.Key).Reverse().ToList())
        {
            shuffled[key] = reordered[key]!.DeepClone();
        }

        DraftFacts first = Facts(await RunAsync(folder, new DraftRequest("MedicationRequest", PotassiumOrder)));
        DraftFacts second = Facts(await RunAsync(folder, new DraftRequest("MedicationRequest", shuffled.ToJsonString())));
        DraftFacts other = Facts(await RunAsync(folder, new DraftRequest("MedicationRequest", PotassiumOrder.Replace("twice daily", "once daily", StringComparison.Ordinal))));
        DraftFacts later = Facts(await RunAsync(folder, new DraftRequest("MedicationRequest", PotassiumOrder), asOf: "2024-05-02T10:00:00Z"));

        Text(second.Resource["id"]).ShouldBe(Text(first.Resource["id"]));
        second.Resource.ToJsonString().ShouldBe(first.Resource.ToJsonString());
        Text(other.Resource["id"]).ShouldNotBe(Text(first.Resource["id"]));
        Text(later.Resource["id"]).ShouldNotBe(Text(first.Resource["id"]));
    }

    [Theory]
    [InlineData("r5")]
    [InlineData("stu3")]
    public async Task GivenAFolderReadAsAnotherVersion_WhenDrafted_ThenTheVersionIsRefused(string version)
    {
        using TestFolder folder = Clinic();
        var session = new FolderSession(folder.Path, FhirSchemas.Resolve(version), NullLoggerFactory.Instance, terminology: Terminology.TerminologyOptions.None);

        IssueItem error = Error(await RunAsync(session, new DraftRequest("ServiceRequest", MagnesiumOrder)));

        error.Code.ShouldBe("draft-unsupported-version");
    }

    [Fact]
    public async Task GivenAnR4BFolder_WhenDrafted_ThenTheDraftIsBuiltAndValidated()
    {
        using TestFolder folder = Clinic();
        var session = new FolderSession(folder.Path, new R4BCoreSchemaProvider(), NullLoggerFactory.Instance, terminology: Terminology.TerminologyOptions.None);

        DraftFacts facts = Facts(await RunAsync(session, new DraftRequest("ServiceRequest", MagnesiumOrder)));

        Check(facts, "validation", "ServiceRequest").Outcome.ShouldNotBe(DraftCheck.Fail);
    }

    [Fact]
    public async Task GivenAnUnknownResourceType_WhenDrafted_ThenItIsARequestError()
    {
        using TestFolder folder = Clinic();

        Error(await RunAsync(folder, new DraftRequest("Procedure", MagnesiumOrder))).Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenATimeoutAlreadyPassed_WhenDrafted_ThenItIsADraftTimeout()
    {
        using TestFolder folder = Clinic();
        FolderSession session = Open(folder);

        ToolEnvelope<DraftFacts?> envelope = await DraftService.RunAsync(session, new DraftRequest("MedicationRequest", PotassiumOrder), new AsOfClock(session, Instant), NullLogger.Instance, seams: null, new CancellationToken(canceled: true), CancellationToken.None);

        Error(envelope).Code.ShouldBe("draft-timeout");
    }

    [Fact]
    public async Task GivenACallerWhoCancels_WhenDrafted_ThenTheCancellationIsNotATimeout()
    {
        using TestFolder folder = Clinic();
        FolderSession session = Open(folder);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => DraftService.RunAsync(session, new DraftRequest("MedicationRequest", PotassiumOrder), new AsOfClock(session, Instant), NullLogger.Instance, cancelled.Token));
    }

    [Fact]
    public async Task GivenADraft_WhenItsEnvelopeIsSerialized_ThenDerivedIsNullAndNextIsEmpty()
    {
        using TestFolder folder = Clinic();

        JsonNode json = TerminologyFixture.Json(await RunAsync(folder, new DraftRequest("ServiceRequest", MagnesiumOrder)));

        json["derived"].ShouldBeNull();
        json["next"]!.AsArray().ShouldBeEmpty();
        Text(json["facts"]!["asOf"]!["instant"]).ShouldBe(Instant);
        Text(json["facts"]!["asOf"]!["source"]).ShouldBe(AsOfSources.SessionOption);
        json["facts"]!["checks"]!.AsArray().Select(check => Text(check!["check"])).Distinct().ShouldBe(["validation", "terminology", "reference"]);
    }
}
