// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Cohorts;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.CohortFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// V15: patient_cohort states its asOf and where it came from, warns when a defaulted asOf is after the folder's data, names the
/// first step that left no patient and why, and says when the criteria name a code system the folder does not use. New at V15:
/// at 9f59d830 (V14) none of these existed, so each test that expects one of the issues, or facts.asOfSource, fails there.
/// </summary>
public class CohortExplanationTests
{
    private const string Icd10 = "http://hl7.org/fhir/sid/icd-10-cm";

    private static FolderSession OpenAt(TestFolder folder, DateTimeOffset now) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: new ManualTimeProvider { Now = now });

    private static TestFolder DatedFolder() => new TestFolder().Write("data.ndjson", Lines(
        Patient("pa", "1970-01-01"),
        Condition("ca", "pa"),
        Lab("a1", "pa", A1c, "2026-03-01", 6.5m)));

    private static CohortArguments PrediabetesCriterion() => new(Conditions: Json($$"""[{"codes":["{{Sct}}|{{Prediabetes}}"]}]"""));

    private static IssueItem? Warning(ToolEnvelope<CohortFacts?> envelope) => envelope.Issues.SingleOrDefault(issue => issue.Code == "asof-after-data");

    private static IReadOnlyList<IssueItem> Explanations(ToolEnvelope<CohortFacts?> envelope) => [.. envelope.Issues.Where(issue => issue.Code == "cohort-empty-step")];

    [Fact]
    public async Task GivenNoAsOfAndDataThatEndsEarlier_WhenCounted_ThenAsOfIsEchoedAsDefaultNowWithAWarningNamingBothDates()
    {
        using TestFolder folder = DatedFolder();

        ToolEnvelope<CohortFacts?> envelope = await PatientCohort.RunAsync(OpenAt(folder, new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)), PrediabetesCriterion(), CancellationToken.None);

        CohortFacts facts = Facts(envelope);
        facts.AsOf.ShouldBe("2026-10-15T12:00:00Z");
        facts.AsOfSource.ShouldBe("default-now");
        IssueItem warning = Warning(envelope).ShouldNotBeNull();
        warning.Severity.ShouldBe("warning");
        warning.Diagnostics.ShouldBe("asOf defaulted to 2026-10-15; the latest dated resource in this folder is 2026-03-01. Pass asOf if the question has its own 'now'.");
    }

    [Fact]
    public async Task GivenAnAsOfArgument_WhenCountedAfterTheData_ThenItsSourceIsArgumentAndThereIsNoWarning()
    {
        using TestFolder folder = DatedFolder();

        ToolEnvelope<CohortFacts?> envelope = await PatientCohort.RunAsync(OpenAt(folder, new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)), PrediabetesCriterion() with { AsOf = "2026-08-17" }, CancellationToken.None);

        CohortFacts facts = Facts(envelope);
        facts.AsOf.ShouldBe("2026-08-17");
        facts.AsOfSource.ShouldBe("argument");
        Warning(envelope).ShouldBeNull();
    }

    [Theory]
    [InlineData(2026, 3, 1)]
    [InlineData(2026, 2, 1)]
    public async Task GivenNoAsOfAndAClockNotAfterTheLatestDate_WhenCounted_ThenThereIsNoWarning(int year, int month, int day)
    {
        using TestFolder folder = DatedFolder();

        ToolEnvelope<CohortFacts?> envelope = await PatientCohort.RunAsync(OpenAt(folder, new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero)), PrediabetesCriterion(), CancellationToken.None);

        Facts(envelope).AsOfSource.ShouldBe("default-now");
        Warning(envelope).ShouldBeNull();
    }

    [Fact]
    public async Task GivenNoAsOfAndNoDatedResources_WhenCounted_ThenThereIsNoWarning()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", Lines(Patient("pa", "1970-01-01"), Condition("ca", "pa")));

        ToolEnvelope<CohortFacts?> envelope = await PatientCohort.RunAsync(OpenAt(folder, new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)), PrediabetesCriterion(), CancellationToken.None);

        Warning(envelope).ShouldBeNull();
    }

    [Fact]
    public async Task GivenCodesNoConditionHas_WhenCounted_ThenTheFirstEmptyStepIsNamedWithHowManyGivenCodesOccur()
    {
        using TestFolder folder = DatedFolder();
        var arguments = new CohortArguments(Conditions: Json($$"""[{"codes":["{{Sct}}|1","{{Sct}}|2"]}]"""));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        Facts(envelope).Count.ShouldBe(0);
        Explanations(envelope).ShouldHaveSingleItem().Diagnostics.ShouldBe("conditions[0] is the first step that left no patient: no Condition in the folder has any of the given codes (0 of 2 given codes occur in the folder).");
        envelope.Issues.Single(issue => issue.Code == "cohort-empty-step").Severity.ShouldBe("information");
    }

    [Fact]
    public async Task GivenConditionsOnlyOfPatientsAnEarlierStepRemoved_WhenCounted_ThenTheStepSaysNoneBelongsToARemainingPatient()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", Lines(
            Patient("pa", "1970-01-01"),
            Patient("pd", "1960-01-01", ""","deceasedDateTime":"2025-01-01" """.TrimEnd()),
            Condition("cd", "pd")));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), PrediabetesCriterion());

        Explanations(envelope).ShouldHaveSingleItem().Diagnostics.ShouldBe("conditions[0] is the first step that left no patient: 1 Condition resources in the folder have those codes, but none belongs to any of the 1 remaining patients.");
    }

    [Fact]
    public async Task GivenAnAgeBandNoPatientIsIn_WhenCounted_ThenTheAgeStepIsNamedAndLaterEmptyStepsSayNothing()
    {
        using TestFolder folder = DatedFolder();
        CohortArguments arguments = PrediabetesCriterion() with { AgeMin = 100 };

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        Explanations(envelope).ShouldHaveSingleItem().Diagnostics.ShouldBe("age is the first step that left no patient: it removed all 1 remaining patients (whole years at asOf from birthDate: 100 or older).");
    }

    [Fact]
    public async Task GivenConditionsThatFailTheStatusRule_WhenCounted_ThenTheStepSaysHowManyPatientsHadThemAndTheRule()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", Lines(
            Patient("pa", "1970-01-01"),
            Patient("pb", "1971-01-01"),
            Condition("ca", "pa", clinical: "resolved"),
            Condition("cb", "pb", clinical: "inactive")));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), PrediabetesCriterion());

        Explanations(envelope).ShouldHaveSingleItem().Diagnostics.ShouldStartWith("conditions[0] is the first step that left no patient: 2 of the 2 remaining patients have a Condition with those codes, but none met the rule: a Condition with one of 1 code, active at asOf");
    }

    [Fact]
    public async Task GivenReadingsOnlyOutsideTheWindow_WhenCounted_ThenTheObservationStepSaysNoneHadAReadingInTheWindow()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", Lines(
            Patient("pa", "1970-01-01"),
            Condition("ca", "pa"),
            Lab("a1", "pa", A1c, "2024-01-01", 8m)));
        var arguments = PrediabetesCriterion() with { Observations = Json($$"""[{"codes":["{{Loinc}}|{{A1c}}"],"window":{"after":"2025-08-17"},"comparator":"gt","value":6.0}]""") };

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        Explanations(envelope).ShouldHaveSingleItem().Diagnostics.ShouldBe("observations[0] is the first step that left no patient: 1 of the 1 remaining patients have an Observation with those codes, but none had a reading in the window (2025-08-17, 2026-08-17] (a final, amended or corrected Observation of those codes with an instant in the window and a numeric value).");
    }

    [Fact]
    public async Task GivenReadingsInTheWindowThatMissTheComparison_WhenCounted_ThenTheObservationStepSaysHowManyHadAReading()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", Lines(
            Patient("pa", "1970-01-01"),
            Condition("ca", "pa"),
            Lab("a1", "pa", A1c, "2026-01-01", 5m)));
        var arguments = PrediabetesCriterion() with { Observations = Json($$"""[{"codes":["{{Loinc}}|{{A1c}}"],"window":{"after":"2025-08-17"},"comparator":"gt","value":6.0}]""") };

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        Explanations(envelope).ShouldHaveSingleItem().Diagnostics.ShouldContain("1 of the 1 remaining patients had a reading in the window (2025-08-17, 2026-08-17], but none met the comparison:");
    }

    [Fact]
    public async Task GivenAFolderWithoutPatients_WhenCounted_ThenThePatientsStepIsNamed()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", Condition("ca", "pa"));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), PrediabetesCriterion());

        Explanations(envelope).ShouldHaveSingleItem().Diagnostics.ShouldBe("patients is the first step that left no patient: the folder has no Patient resources.");
    }

    [Fact]
    public async Task GivenACohortThatCountsPatients_WhenCounted_ThenNoStepIsExplained()
    {
        using TestFolder folder = DatedFolder();

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), PrediabetesCriterion());

        Facts(envelope).Count.ShouldBe(1);
        Explanations(envelope).ShouldBeEmpty();
        envelope.Issues.ShouldNotContain(issue => issue.Code == "code-system-not-in-folder");
    }

    [Fact]
    public async Task GivenAConditionSystemTheFolderDoesNotUse_WhenTheCohortIsEmpty_ThenTheIssueNamesTheSystemAndWhatConditionsUse()
    {
        using TestFolder folder = DatedFolder();
        var arguments = new CohortArguments(Conditions: Json($$"""[{"codes":["{{Icd10}}|I25"]}]"""));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        envelope.Issues.Single(issue => issue.Code == "code-system-not-in-folder").Diagnostics.ShouldBe($"No Condition in this folder is coded in {Icd10}; Conditions here use {Sct}. Search the folder's codes by words (folder_code_systems text=...) and use those.");
    }

    [Fact]
    public async Task GivenAnUnusedSystemBesideAUsedCode_WhenTheCohortCountsPatients_ThenThereIsNoCodeSystemIssue()
    {
        using TestFolder folder = DatedFolder();
        var arguments = new CohortArguments(Conditions: Json($$"""[{"codes":["{{Icd10}}|I25","{{Sct}}|{{Prediabetes}}"]}]"""));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        Facts(envelope).Count.ShouldBe(1);
        envelope.Issues.ShouldNotContain(issue => issue.Code == "code-system-not-in-folder");
    }

    [Fact]
    public async Task GivenAUsedSystemWithAMissingCode_WhenTheCohortIsEmpty_ThenOnlyTheStepIsExplained()
    {
        using TestFolder folder = DatedFolder();
        var arguments = new CohortArguments(Conditions: Json($$"""[{"codes":["{{Sct}}|1"]}]"""));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        envelope.Issues.ShouldNotContain(issue => issue.Code == "code-system-not-in-folder");
        Explanations(envelope).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenABareCodeNoCodingHolds_WhenTheCohortIsEmpty_ThenTheIssueListsTheSystemsTheTypeUses()
    {
        using TestFolder folder = DatedFolder();
        var arguments = new CohortArguments(Conditions: Json("""[{"codes":["I25"]}]"""));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        envelope.Issues.Single(issue => issue.Code == "code-system-not-in-folder").Diagnostics.ShouldStartWith($"No Condition in this folder has the code I25 in any coding system; Conditions here use {Sct}.");
    }

    [Fact]
    public async Task GivenAMedicationCodeHeldOnlyByAReferencedMedication_WhenTheCohortIsEmpty_ThenThereIsNoCodeSystemIssueForIt()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", Lines(
            Patient("pa", "1970-01-01"),
            Medication("m1", "314231"),
            OrderByReference("o1", "pa", "m1", status: "stopped"),
            Order("o2", "pa", "860975")));
        var arguments = new CohortArguments(Medications: Json("""[{"codes":["314231"],"status":"active"}]"""));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        Facts(envelope).Count.ShouldBe(0);
        envelope.Issues.ShouldNotContain(issue => issue.Code == "code-system-not-in-folder");
    }

    [Fact]
    public async Task GivenAMedicationCodeNeitherAnOrderNorAMedicationHolds_WhenTheCohortIsEmpty_ThenTheIssueNamesTheOrderType()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", Lines(
            Patient("pa", "1970-01-01"),
            Medication("m1", "314231"),
            Order("o2", "pa", "860975")));
        var arguments = new CohortArguments(Medications: Json("""[{"codes":["999999"]}]"""));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), arguments);

        envelope.Issues.Single(issue => issue.Code == "code-system-not-in-folder").Diagnostics.ShouldStartWith($"No MedicationRequest in this folder has the code 999999 in any coding system; MedicationRequests here use {RxNorm}.");
    }
}
