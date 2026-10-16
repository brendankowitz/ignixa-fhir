// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Cohorts;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.CohortFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// patient_cohort: a quality-measure-shaped population count in one call. New capability (V10): at c812a343 there was no
/// such tool, and agents built cohorts from 6 to 9 row-listing calls or a hand-written patientSearch expression.
/// </summary>
public class PatientCohortTests
{
    private const string LatestA1cAboveSix = $$"""[{"codes":["{{Loinc}}|{{A1c}}"],"window":{"after":"2025-08-17"},"comparator":"gt","value":6.0,"missing":"include"}]""";

    /// <summary>
    /// Prediabetes, as of 2026-08-17: pa's latest A1c in the year is 6.5; pb's is 5.5 (after a 7.0); pc has none in the year
    /// (8.0 in 2024); pd is deceased; pe has no prediabetes; pf's readings sit on the window's edges: 9 at exactly the
    /// exclusive start, 5.0 at exactly asOf (inclusive), 9 later on asOf's day (after asOf); pg's only reading, 9, is at
    /// exactly the exclusive start, so pg has none in the window.
    /// </summary>
    private static TestFolder Prediabetic() => new TestFolder()
        .Write("patients.ndjson", Lines(
            Patient("pa", "1970-01-01"),
            Patient("pb", "1980-05-05"),
            Patient("pc", "1990-08-17"),
            Patient("pd", "1960-01-01", ""","deceasedDateTime":"2025-01-01" """.TrimEnd()),
            Patient("pe", "1985-01-01"),
            Patient("pf", "1985-02-02"),
            Patient("pg", "1985-03-03")))
        .Write("conditions.ndjson", Lines(Condition("ca", "pa"), Condition("cb", "pb"), Condition("cc", "pc"), Condition("cd", "pd"), Condition("cf", "pf"), Condition("cg", "pg")))
        .Write("labs.ndjson", Lines(
            Lab("a1", "pa", A1c, "2026-01-10", 6.5m),
            Lab("b1", "pb", A1c, "2025-12-01", 7.0m),
            Lab("b2", "pb", A1c, "2026-03-01", 5.5m),
            Lab("c1", "pc", A1c, "2024-01-01", 8.0m),
            Lab("f1", "pf", A1c, "2025-08-17T00:00:00Z", 9m),
            Lab("f3", "pf", A1c, "2026-08-17", 5.0m),
            Lab("f2", "pf", A1c, "2026-08-17T10:00:00Z", 9m),
            Lab("g1", "pg", A1c, "2025-08-17T00:00:00Z", 9m)));

    private static CohortArguments PrediabetesQuestion(string missing = "include") => new(
        AgeMin: 18,
        AgeMax: 75,
        Conditions: Json($$"""[{"codes":["{{Sct}}|{{Prediabetes}}"]}]"""),
        Observations: Json(LatestA1cAboveSix.Replace("\"include\"", $"\"{missing}\"", StringComparison.Ordinal)));

    [Fact]
    public async Task GivenAConditionAndALatestLabWithMissingIncluded_WhenCountedInFull_ThenTheCountStepsRulesPatientsAndDecidingEvidenceAreReturned()
    {
        using TestFolder folder = Prediabetic();

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), PrediabetesQuestion() with { Detail = "full" });

        CohortFacts facts = Facts(envelope);
        facts.Count.ShouldBe(3);
        facts.AsOf.ShouldBe(AsOf);
        facts.Patients.ShouldBe(["Patient/pa", "Patient/pc", "Patient/pg"]);
        Steps(facts).ShouldBe([("patients", 7), ("living", 6), ("age", 6), ("conditions[0]", 5), ("observations[0]", 3)]);
        facts.Steps[^1].WithoutReading.ShouldBe(2);
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Patient/pa", "Condition/ca", "Observation/a1", "Patient/pc", "Condition/cc", "Patient/pg", "Condition/cg"]);
        envelope.Omitted.ShouldBeEmpty();
        CohortStep labStep = facts.Steps[^1];
        labStep.Rule.ShouldNotBeNull().ShouldContain("the latest");
        labStep.Rule.ShouldContain("(2025-08-17, 2026-08-17]");
        labStep.Rule.ShouldContain("> 6.0");
    }

    // New at V13: at b3a34f4e (V12) every step repeated its rule sentence and every listed patient's evidence was returned.
    [Fact]
    public async Task GivenACountedCohort_WhenCountedByDefault_ThenStepsCarryNoRuleAndEvidenceStopsAfterTheFirstPatients()
    {
        using TestFolder folder = Prediabetic();

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), PrediabetesQuestion());

        CohortFacts facts = Facts(envelope);
        facts.Count.ShouldBe(3);
        facts.Patients.ShouldBe(["Patient/pa", "Patient/pc", "Patient/pg"]);
        Steps(facts).ShouldBe([("patients", 7), ("living", 6), ("age", 6), ("conditions[0]", 5), ("observations[0]", 3)]);
        facts.Steps.ShouldAllBe(step => step.Rule == null);
        facts.Steps[^1].WithoutReading.ShouldBe(2);
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Patient/pa", "Condition/ca", "Observation/a1", "Patient/pc", "Condition/cc"]);
        envelope.Omitted.ShouldHaveSingleItem().What.ShouldContain("detail=full");
        envelope.Next.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenMoreCountedPatientsThanAreListed_WhenCountedByDefault_ThenTheListIsCappedAndFullDetailListsThemAll()
    {
        const int patients = 30;
        using TestFolder folder = new TestFolder()
            .Write("patients.ndjson", Lines([.. Enumerable.Range(0, patients).Select(index => Patient($"p{index:D2}", "1970-01-01"))]))
            .Write("conditions.ndjson", Lines([.. Enumerable.Range(0, patients).Select(index => Condition($"c{index}", $"p{index:D2}"))]));
        var arguments = new CohortArguments(Conditions: Json($$"""{"codes":["{{Prediabetes}}"]}"""));

        CohortFacts compact = Facts(await Count(SearchFixture.Open(folder), arguments));
        ToolEnvelope<CohortFacts?> full = await Count(SearchFixture.Open(folder), arguments with { Detail = "full" });

        compact.Count.ShouldBe(patients);
        compact.Patients.Count.ShouldBe(PatientCohort.PatientsListed);
        Facts(full).Patients.Count.ShouldBe(patients);
        full.Next.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenAnUnknownDetail_WhenCounted_ThenTheCallFailsAsInvalidParameter()
    {
        using TestFolder folder = Prediabetic();

        IssueItem issue = Failure(await Count(SearchFixture.Open(folder), PrediabetesQuestion() with { Detail = "everything" }));

        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain("'compact' (the default) or 'full'");
    }

    [Fact]
    public async Task GivenMissingExcluded_WhenCounted_ThenAPatientWithoutAReadingInTheWindowIsNotCounted()
    {
        using TestFolder folder = Prediabetic();

        CohortFacts facts = Facts(await Count(SearchFixture.Open(folder), PrediabetesQuestion(missing: "exclude")));

        facts.Patients.ShouldBe(["Patient/pa"]);
        facts.Steps[^1].WithoutReading.ShouldBeNull();
    }

    [Fact]
    public async Task GivenACountedCohort_WhenAnsweredInFull_ThenNextListsThePatientsAsATable()
    {
        using TestFolder folder = Prediabetic();

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), PrediabetesQuestion() with { Detail = "full" });

        NextCall next = envelope.Next.ShouldHaveSingleItem();
        next.Tool.ShouldBe("search_fhir_resources");
        next.Args["resourceType"].ShouldBe("Patient");
        next.Args["query"].ShouldBe("_id=pa,pc,pg");
        next.Args.ShouldContainKey("columns");
    }

    [Fact]
    public async Task GivenCriteriaAsJsonArgumentsOverMcp_WhenCalled_ThenTheyBindAndTheCohortIsCountedInOneCall()
    {
        using TestFolder folder = Prediabetic();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(PatientTools)], SearchFixture.Open(folder));
        var arguments = new Dictionary<string, object?>
        {
            ["asOf"] = AsOf,
            ["ageMin"] = 18,
            ["ageMax"] = 75,
            ["conditions"] = Json($$"""[{"codes":["{{Sct}}|{{Prediabetes}}"]}]"""),
            ["observations"] = Json(LatestA1cAboveSix),
        };

        CallToolResult result = await mcp.Client.CallToolAsync("patient_cohort", arguments, cancellationToken: mcp.CancellationToken);

        result.IsError.ShouldNotBe(true);
        JsonNode envelope = TerminologyFixture.Json(result);
        ((int)envelope["facts"]!["count"]!).ShouldBe(3);
        envelope["facts"]!["patients"]!.AsArray().Select(patient => (string)patient!).ShouldBe(["Patient/pa", "Patient/pc", "Patient/pg"]);
    }

    [Fact]
    public async Task GivenAnArgumentTheToolDoesNotDefine_WhenCalledOverMcp_ThenTheCallFailsNamingItAndTheToolsArguments()
    {
        using TestFolder folder = Prediabetic();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(PatientTools)], SearchFixture.Open(folder));
        var arguments = new Dictionary<string, object?> { ["asOf"] = AsOf, ["condition"] = Json("""{"codes":["714628002"]}""") };

        CallToolResult result = await mcp.Client.CallToolAsync("patient_cohort", arguments, cancellationToken: mcp.CancellationToken);

        result.IsError.ShouldBe(true);
        JsonNode issue = TerminologyFixture.Json(result)["issues"]![0]!;
        ((string)issue["code"]!).ShouldBe("invalid-parameter");
        ((string)issue["diagnostics"]!).ShouldContain("'condition' is not an argument of this tool");
        ((string)issue["diagnostics"]!).ShouldContain("conditions");
    }

    [Theory]
    [InlineData(18, null, new[] { "Patient/born-2008-08-17", "Patient/born-1950-08-18", "Patient/born-1950-08-17" })]
    [InlineData(null, 75, new[] { "Patient/born-2008-08-17", "Patient/born-2008-08-18", "Patient/born-1950-08-18" })]
    [InlineData(18, 75, new[] { "Patient/born-2008-08-17", "Patient/born-1950-08-18" })]
    public async Task GivenBirthdaysOnTheBoundaries_WhenAnAgeBandIsAsked_ThenAgeIsWholeYearsAtAsOfAndBothEndsAreInclusive(int? min, int? max, string[] expected)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Lines(
            Patient("born-2008-08-17", "2008-08-17"),
            Patient("born-2008-08-18", "2008-08-18"),
            Patient("born-1950-08-18", "1950-08-18"),
            Patient("born-1950-08-17", "1950-08-17"),
            Patient("born-1990", "1990")));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), new CohortArguments(AgeMin: min, AgeMax: max));

        Facts(envelope).Patients.ShouldBe(expected);
        envelope.Issues.ShouldContain(issue => issue.Code == "age-unknown" && issue.Diagnostics.StartsWith("1 patients", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenDeathsRecordedEachWay_WhenLivingIsDefaultOrFalse_ThenOnlyThoseAliveAtAsOfOrEveryoneAreCounted()
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Lines(
            Patient("alive", "1970-01-01"),
            Patient("died-flag", "1970-01-01", ""","deceasedBoolean":true"""),
            Patient("not-died-flag", "1970-01-01", ""","deceasedBoolean":false"""),
            Patient("died-before", "1970-01-01", ""","deceasedDateTime":"2026-08-16" """.TrimEnd()),
            Patient("dies-after", "1970-01-01", ""","deceasedDateTime":"2026-08-18" """.TrimEnd())));
        FolderSession session = SearchFixture.Open(folder);

        CohortFacts living = Facts(await Count(session, new CohortArguments()));
        CohortFacts everyone = Facts(await Count(session, new CohortArguments(Living: false)));

        living.Patients.ShouldBe(["Patient/alive", "Patient/not-died-flag", "Patient/dies-after"]);
        everyone.Count.ShouldBe(5);
        Steps(everyone).ShouldBe([("patients", 5)]);
    }

    [Fact]
    public async Task GivenGender_WhenAsked_ThenOnlyPatientsOfThatGenderAreCounted()
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Lines(
            Patient("f1", "1970-01-01", ""","gender":"female" """.TrimEnd()),
            Patient("m1", "1970-01-01", ""","gender":"male" """.TrimEnd()),
            Patient("u1", "1970-01-01")));

        CohortFacts facts = Facts(await Count(SearchFixture.Open(folder), new CohortArguments(Gender: "female")));

        facts.Patients.ShouldBe(["Patient/f1"]);
    }

    /// <summary>One patient per Condition: p1 active, p2 resolved, p3 abated before asOf, p4 abated after it, p5 onset after it,
    /// p6 refuted, p7 entered in error, p8 recurrence, p9 provisional.</summary>
    private static TestFolder ConditionStatuses() => new TestFolder()
        .Write("patients.ndjson", Lines([.. Enumerable.Range(1, 9).Select(index => Patient($"p{index}", "1970-01-01"))]))
        .Write("conditions.ndjson", Lines(
            Condition("c1", "p1"),
            Condition("c2", "p2", clinical: "resolved"),
            Condition("c3", "p3", extra: ""","abatementDateTime":"2026-01-01" """.TrimEnd()),
            Condition("c4", "p4", extra: ""","abatementDateTime":"2026-09-01" """.TrimEnd()),
            Condition("c5", "p5", extra: ""","onsetDateTime":"2026-09-01" """.TrimEnd()),
            Condition("c6", "p6", extra: Verification("refuted")),
            Condition("c7", "p7", extra: Verification("entered-in-error")),
            Condition("c8", "p8", clinical: "recurrence"),
            Condition("c9", "p9", extra: Verification("provisional"))));

    [Theory]
    [InlineData("""{"codes":["http://snomed.info/sct|714628002"]}""", new[] { "Patient/p1", "Patient/p4", "Patient/p8" })]
    [InlineData("""{"codes":["714628002"],"status":"any"}""", new[] { "Patient/p1", "Patient/p2", "Patient/p3", "Patient/p4", "Patient/p8" })]
    [InlineData("""{"codes":["714628002"],"verification":"any"}""", new[] { "Patient/p1", "Patient/p4", "Patient/p6", "Patient/p8", "Patient/p9" })]
    [InlineData("""{"codes":["714628002"],"status":"any","verification":"any"}""", new[] { "Patient/p1", "Patient/p2", "Patient/p3", "Patient/p4", "Patient/p6", "Patient/p8", "Patient/p9" })]
    public async Task GivenConditionsInEveryStatus_WhenCounted_ThenStatusVerificationOnsetAndAbatementAreJudgedAtAsOf(string criterion, string[] expected)
    {
        using TestFolder folder = ConditionStatuses();

        CohortFacts facts = Facts(await Count(SearchFixture.Open(folder), new CohortArguments(Conditions: Json(criterion))));

        facts.Patients.ShouldBe(expected);
    }

    /// <summary>
    /// Statin 314231 for p1 inline and p2 through a Medication; p3's order stopped; p4's says not to give it; p5's is authored
    /// after asOf; p6's entered in error; p7's is another drug.
    /// </summary>
    private static TestFolder Orders() => new TestFolder()
        .Write("patients.ndjson", Lines([.. Enumerable.Range(1, 7).Select(index => Patient($"p{index}", "1970-01-01"))]))
        .Write("orders.ndjson", Lines(
            Order("m1", "p1", "314231"),
            OrderByReference("m2", "p2", "med1"),
            Medication("med1", "314231"),
            Order("m3", "p3", "314231", status: "stopped"),
            Order("m4", "p4", "314231", extra: ""","doNotPerform":true"""),
            Order("m5", "p5", "314231", authoredOn: "2026-09-01"),
            Order("m6", "p6", "314231", status: "entered-in-error"),
            Order("m7", "p7", "860975")));

    [Theory]
    [InlineData("""{"codes":["http://www.nlm.nih.gov/research/umls/rxnorm|314231"]}""", new[] { "Patient/p1", "Patient/p2" })]
    [InlineData("""{"codes":["314231"],"status":"any"}""", new[] { "Patient/p1", "Patient/p2", "Patient/p3" })]
    public async Task GivenOrdersInBothMedicationShapes_WhenCounted_ThenEachShapeMatchesAndStatusDoNotPerformAndAuthoredOnAreJudged(string criterion, string[] expected)
    {
        using TestFolder folder = Orders();

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), new CohortArguments(Medications: Json(criterion)));

        Facts(envelope).Patients.ShouldBe(expected);
        envelope.Evidence.Select(item => item.Reference).ShouldContain("MedicationRequest/m2");
    }

    [Theory]
    [InlineData(null, new[] { "Patient/p1", "Patient/p3" })]
    [InlineData("""{"after":"2025-08-17"}""", new[] { "Patient/p3" })]
    public async Task GivenImmunizations_WhenCountedEverOrInAWindow_ThenOnlyCompletedOnesWithAListedCodeAtOrBeforeAsOfCount(string? window, string[] expected)
    {
        using TestFolder folder = new TestFolder()
            .Write("patients.ndjson", Lines([.. Enumerable.Range(1, 5).Select(index => Patient($"p{index}", "1950-01-01"))]))
            .Write("immunizations.ndjson", Lines(
                Immunization("i1", "p1", "33", "2010-01-01"),
                Immunization("i2", "p2", "33", "2010-01-01", status: "not-done"),
                Immunization("i3", "p3", "133", "2026-01-01"),
                Immunization("i4", "p4", "33", "2026-09-01"),
                Immunization("i5", "p5", "08", "2010-01-01")));
        string criterion = window is null ? $$"""{"codes":["{{Cvx}}|33","{{Cvx}}|133"]}""" : $$"""{"codes":["{{Cvx}}|33","{{Cvx}}|133"],"window":{{window}}}""";

        CohortFacts facts = Facts(await Count(SearchFixture.Open(folder), new CohortArguments(Immunizations: Json(criterion))));

        facts.Patients.ShouldBe(expected);
    }

    /// <summary>
    /// LDL: p1 150 (2025) then 100 (2026); p2 4.0 mmol/L; p3 two readings at the same instant, o4 140 and o5 50; p4 a blood
    /// pressure panel whose systolic component is 150; p5 a preliminary 200.
    /// </summary>
    private static TestFolder Readings() => new TestFolder()
        .Write("patients.ndjson", Lines([.. Enumerable.Range(1, 5).Select(index => Patient($"p{index}", "1970-01-01"))]))
        .Write("labs.ndjson", Lines(
            Lab("o1", "p1", Ldl, "2025-01-01", 150, "mg/dL"),
            Lab("o2", "p1", Ldl, "2026-01-01", 100, "mg/dL"),
            Lab("o3", "p2", Ldl, "2026-02-01", 4.0m, "mmol/L"),
            Lab("o5", "p3", Ldl, "2026-03-01T09:00:00Z", 50, "mg/dL"),
            Lab("o4", "p3", Ldl, "2026-03-01T09:00:00Z", 140, "mg/dL"),
            Lab("o6", "p5", Ldl, "2026-03-01", 200, "mg/dL", status: "preliminary"),
            """{"resourceType":"Observation","id":"bp1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"85354-9"}]},"subject":{"reference":"Patient/p4"},"effectiveDateTime":"2026-04-01","component":[{"code":{"coding":[{"system":"http://loinc.org","code":"8462-4"}]},"valueQuantity":{"value":95,"code":"mm[Hg]"}},{"code":{"coding":[{"system":"http://loinc.org","code":"8480-6"}]},"valueQuantity":{"value":150,"code":"mm[Hg]"}}]}"""));

    [Theory]
    [InlineData("latest", new[] { "Patient/p3" })]
    [InlineData("earliest", new[] { "Patient/p1", "Patient/p3" })]
    [InlineData("any", new[] { "Patient/p1", "Patient/p3" })]
    public async Task GivenSeveralReadings_WhenPickedLatestEarliestOrAny_ThenTheChosenReadingIsComparedAndTiesGoToTheSmallerId(string pick, string[] expected)
    {
        using TestFolder folder = Readings();
        string criterion = $$"""{"codes":["{{Loinc}}|{{Ldl}}"],"pick":"{{pick}}","comparator":"ge","value":130}""";

        CohortFacts facts = Facts(await Count(SearchFixture.Open(folder), new CohortArguments(Observations: Json(criterion))));

        facts.Patients.ShouldBe(expected);
    }

    [Fact]
    public async Task GivenAUnit_WhenAReadingIsInAnotherUnit_ThenItIsNotComparedOrConvertedAndAWarningSaysHowMany()
    {
        using TestFolder folder = Readings();
        FolderSession session = SearchFixture.Open(folder);

        ToolEnvelope<CohortFacts?> withUnit = await Count(session, new CohortArguments(Observations: Json($$"""{"codes":["{{Ldl}}"],"comparator":"lt","value":200,"unit":"mg/dL"}""")));
        ToolEnvelope<CohortFacts?> asRecorded = await Count(session, new CohortArguments(Observations: Json($$"""{"codes":["{{Ldl}}"],"comparator":"lt","value":200}""")));

        Facts(withUnit).Patients.ShouldBe(["Patient/p1", "Patient/p3"]);
        IssueItem mismatch = withUnit.Issues.Single(issue => issue.Code == "unit-mismatch");
        mismatch.Severity.ShouldBe("warning");
        mismatch.Diagnostics.ShouldContain("'mmol/L' for 1");
        Facts(asRecorded).Patients.ShouldBe(["Patient/p1", "Patient/p2", "Patient/p3"]);
        asRecorded.Issues.ShouldNotContain(issue => issue.Code == "unit-mismatch");
    }

    [Fact]
    public async Task GivenACodeCarriedByAComponent_WhenCounted_ThenTheComponentsValueIsTheReading()
    {
        using TestFolder folder = Readings();

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), new CohortArguments(Observations: Json($$"""{"codes":["{{Loinc}}|8480-6"],"comparator":"gt","value":140}""")));

        Facts(envelope).Patients.ShouldBe(["Patient/p4"]);
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Patient/p4", "Observation/bp1"]);
    }

    [Fact]
    public async Task GivenNoComparison_WhenCounted_ThenAnyFinalReadingInTheWindowMeetsIt()
    {
        using TestFolder folder = Readings();

        CohortFacts facts = Facts(await Count(SearchFixture.Open(folder), new CohortArguments(Observations: Json($$$"""{"codes":["{{{Ldl}}}"],"window":{"after":"2025-12-31"}}"""))));

        facts.Patients.ShouldBe(["Patient/p1", "Patient/p2", "Patient/p3"]);
    }

    [Fact]
    public async Task GivenSeveralCriteria_WhenCounted_ThenTheyAreAndedInOrderAndEachStepCountsWhatIsLeft()
    {
        using TestFolder folder = new TestFolder()
            .Write("patients.ndjson", Lines([.. Enumerable.Range(1, 4).Select(index => Patient($"p{index}", "1970-01-01"))]))
            .Write("conditions.ndjson", Lines(
                Condition("a1", "p1", "44054006"), Condition("b1", "p1", "38341003"),
                Condition("a2", "p2", "44054006"), Condition("b2", "p2", "38341003"),
                Condition("a3", "p3", "44054006"),
                Condition("b4", "p4", "38341003")))
            .Write("orders.ndjson", Order("m1", "p1", "314231"));
        var arguments = new CohortArguments(
            Conditions: Json("""[{"codes":["44054006"]},{"codes":["38341003"]}]"""),
            Medications: Json("""{"codes":["314231"]}"""));

        CohortFacts facts = Facts(await Count(SearchFixture.Open(folder), arguments));

        Steps(facts).ShouldBe([("patients", 4), ("living", 4), ("conditions[0]", 3), ("conditions[1]", 2), ("medications[0]", 1)]);
        facts.Patients.ShouldBe(["Patient/p1"]);
    }

    [Fact]
    public async Task GivenMorePatientsThanFit_WhenCounted_ThenTheCountIsExactAndThePatientListIsCutToTheBudgetWithAnOmittedItem()
    {
        const int patients = 400;
        using TestFolder folder = new TestFolder()
            .Write("patients.ndjson", Lines([.. Enumerable.Range(0, patients).Select(index => Patient($"patient-{index:D4}-0123456789abcdef", "1970-01-01"))]))
            .Write("conditions.ndjson", Lines([.. Enumerable.Range(0, patients).Select(index => Condition($"c{index}", $"patient-{index:D4}-0123456789abcdef"))]));

        ToolEnvelope<CohortFacts?> envelope = await Count(SearchFixture.Open(folder), new CohortArguments(Conditions: Json($$"""{"codes":["{{Prediabetes}}"]}""")));

        CohortFacts facts = Facts(envelope);
        facts.Count.ShouldBe(patients);
        facts.Patients.Count.ShouldBeInRange(1, patients - 1);
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
        envelope.Omitted.ShouldContain(item => item.Why == "size-budget" && item.What.StartsWith($"{patients - facts.Patients.Count} more patients counted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenMoreResourcesToReadThanTheLimit_WhenCounted_ThenTheCallFailsAsTooManyMatches()
    {
        using TestFolder folder = Prediabetic();

        IssueItem issue = Failure(await Count(SearchFixture.Open(folder), PrediabetesQuestion(), maxRead: 8));

        issue.Code.ShouldBe("too-many-matches");
    }

    [Fact]
    public async Task GivenNoAsOf_WhenCounted_ThenTheSessionClockIsAsOf()
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Patient("p1", "1970-01-01"));
        var session = new FolderSession(folder.Path, new Ignixa.Specification.Generated.R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: new ManualTimeProvider());

        CohortFacts facts = Facts(await PatientCohort.RunAsync(session, new CohortArguments(), CancellationToken.None));

        facts.AsOf.ShouldBe("2026-10-15T12:00:00Z");
    }

    [Theory]
    [InlineData("conditions", """[{"code":["44054006"]}]""", "has 'code', which it does not take")]
    [InlineData("conditions", """[{"status":"active"}]""", "needs 'codes'")]
    [InlineData("conditions", "\"44054006\"", "is a list of criterion objects")]
    [InlineData("conditions", """[{"codes":["|44054006"]}]""", "is not a code")]
    [InlineData("conditions", """[{"codes":["44054006"],"status":"current"}]""", "'active' or 'any'")]
    [InlineData("medications", """[{"codes":["314231"],"source":"ATC"}]""", "'classId', which is not given")]
    [InlineData("medications", """[{"status":"active"}]""", "needs 'codes'")]
    [InlineData("immunizations", """[{"codes":["33"],"ever":true,"window":{"after":"2020-01-01"}}]""", "both ever=true and a window")]
    [InlineData("observations", """[{"codes":["4548-4"],"comparator":"gt"}]""", "'comparator' and 'value' together")]
    [InlineData("observations", """[{"codes":["4548-4"],"missing":"include"}]""", "every patient would meet it")]
    [InlineData("observations", """[{"codes":["4548-4"],"window":{"after":"2026-08-17"}}]""", "is empty")]
    [InlineData("observations", """[{"codes":["4548-4"],"window":{"after":"last year"}}]""", "is not a date")]
    [InlineData("observations", """[{"codes":["4548-4"],"pick":"newest","comparator":"gt","value":6}]""", "'latest' or 'earliest' or 'any'")]
    [InlineData("observations", """[{"codes":["4548-4"],"comparator":"gt","value":"high"}]""", "is a number")]
    public async Task GivenAMalformedCriterion_WhenCounted_ThenTheCallFailsAsInvalidParameterSayingWhatIsWrong(string argument, string json, string message)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Patient("p1", "1970-01-01"));
        CohortArguments arguments = argument switch
        {
            "conditions" => new CohortArguments(Conditions: Json(json)),
            "medications" => new CohortArguments(Medications: Json(json)),
            "immunizations" => new CohortArguments(Immunizations: Json(json)),
            _ => new CohortArguments(Observations: Json(json)),
        };

        IssueItem issue = Failure(await Count(SearchFixture.Open(folder), arguments));

        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain(message);
    }

    [Theory]
    [InlineData("yesterday", null, null, null, "asOf 'yesterday' is not a date")]
    [InlineData(null, 80, 18, null, "ageMin <= ageMax")]
    [InlineData(null, -1, null, null, "at least 0")]
    [InlineData(null, null, null, "f", "gender is male, female, other, unknown")]
    public async Task GivenMalformedDemographics_WhenCounted_ThenTheCallFailsAsInvalidParameter(string? asOf, int? ageMin, int? ageMax, string? gender, string message)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Patient("p1", "1970-01-01"));

        IssueItem issue = Failure(await Count(SearchFixture.Open(folder), new CohortArguments(asOf, AgeMin: ageMin, AgeMax: ageMax, Gender: gender)));

        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain(message);
    }
}
