// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>perPatient</c>: one match per patient, by the Patient compartment, before paging, columns, fhirpath and summary=count,
/// so a cohort question is one call. New capability: at d3498e32 a cohort needed every row paged out and grouped by the caller.
/// </summary>
public class PerPatientSearchTests
{
    private const string Ldl = "code=http://loinc.org|18262-6";

    private const string Living = "subject.resolve().deceased.exists().not()";

    private const string Adult = "subject.resolve().birthDate <= @2008-08-17";

    /// <summary>
    /// LDL results: <c>pa</c> 150 then 110 (latest below 130), <c>pb</c> 160 but deceased, <c>pc</c> 140 but a minor, <c>pe</c>
    /// 100 then 135 (latest at or above 130). <c>pd</c> has no LDL.
    /// </summary>
    private static TestFolder Cohort() => new TestFolder()
        .Write("patients.ndjson", """
            {"resourceType":"Patient","id":"pa","birthDate":"1970-01-01"}
            {"resourceType":"Patient","id":"pb","birthDate":"1960-01-01","deceasedDateTime":"2025-12-01"}
            {"resourceType":"Patient","id":"pc","birthDate":"2015-01-01"}
            {"resourceType":"Patient","id":"pd","birthDate":"1980-01-01"}
            {"resourceType":"Patient","id":"pe","birthDate":"1975-01-01"}
            """)
        .Write("obs.ndjson", string.Join('\n',
            Lab("a1", "pa", "2024-01-01", 150),
            Lab("e1", "pe", "2023-01-01", 100),
            Lab("b1", "pb", "2025-01-01", 160),
            Lab("a2", "pa", "2025-06-01", 110),
            Lab("c1", "pc", "2025-02-01", 140),
            Lab("e2", "pe", "2025-03-01", 135)));

    private static string Lab(string id, string patient, string date, int value) =>
        $$$"""{"resourceType":"Observation","id":"{{{id}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"18262-6"}]},"subject":{"reference":"Patient/{{{patient}}}"},"effectiveDateTime":"{{{date}}}","valueQuantity":{"value":{{{value}}},"unit":"mg/dL"}}""";

    private static SearchRequest Labs(string pick, string query = Ldl) => new("Observation", query, PerPatient: pick);

    [Fact]
    public void GivenSeveralLabsPerPatient_WhenSearchedForTheLatest_ThenEachPatientsLatestIsKeptAndTheSelectionIsReported()
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs(PerPatient.Latest));

        Ids(envelope).ShouldBe(["a2", "e2", "c1", "b1"]);
        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Total.ShouldBe(4);
        facts.PerPatient.ShouldBe(new PerPatientFacts(PerPatient.Latest, "-date", Matches: 6, Patients: 4, Kept: 4, WithoutPatient: 0));
    }

    [Fact]
    public void GivenSeveralLabsPerPatient_WhenSearchedForTheEarliest_ThenEachPatientsEarliestIsKept()
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs(PerPatient.Earliest));

        Ids(envelope).ShouldBe(["e1", "a1", "b1", "c1"]);
        envelope.Facts.ShouldNotBeNull().PerPatient.ShouldNotBeNull().Sort.ShouldBe("date");
    }

    [Fact]
    public void GivenASortOfTheCallersOwn_WhenSearchedForTheFirst_ThenEachPatientsFirstInThatOrderIsKept()
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs(PerPatient.First, Ldl + "&_sort=-value-quantity"));

        Ids(envelope).ShouldBe(["b1", "a1", "c1", "e2"]);
        envelope.Facts.ShouldNotBeNull().PerPatient.ShouldNotBeNull().Sort.ShouldBeNull();
    }

    [Theory]
    [InlineData(PerPatient.First, Ldl, "has no _sort")]
    [InlineData("newest", Ldl, "'perPatient' takes")]
    public void GivenAPickThatCannotBeApplied_WhenSearched_ThenTheCallFailsSayingWhatToChange(string pick, string query, string message)
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs(pick, query));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain(message);
    }

    [Fact]
    public void GivenATypeNeverInAPatientCompartment_WhenSearchedPerPatient_ThenTheCallFails()
    {
        using TestFolder folder = new TestFolder().Write("org.json", """{"resourceType":"Organization","id":"o1","name":"Acme"}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Organization", "_sort=name", PerPatient: PerPatient.First));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldContain("never in a Patient compartment");
    }

    [Fact]
    public void GivenSummaryCount_WhenSearchedPerPatient_ThenTheTotalIsTheNumberOfDistinctPatients()
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs(PerPatient.Latest) with { Summary = "count" });

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Total.ShouldBe(4);
        facts.Resources.ShouldBeNull();
        facts.PerPatient.ShouldNotBeNull().Patients.ShouldBe(4);
    }

    [Fact]
    public void GivenAThreshold_WhenFhirPathCountsThePerPatientSet_ThenOnlyEachPatientsLatestValueIsTested()
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs(PerPatient.Latest) with { FhirPath = "entry.resource.where(value.value >= 130).count()" });

        // a1 (150) is pa's earlier result, so pa does not count; pb, pc and pe do.
        envelope.Facts.ShouldNotBeNull().Value.ShouldNotBeNull().ToJsonString().ShouldBe("[3]");
        envelope.Facts.PerPatient.ShouldNotBeNull().Kept.ShouldBe(4);
    }

    [Fact]
    public void GivenLivingAndAgeFiltersOnTheSubject_WhenFhirPathCountsThePerPatientSet_ThenOnlyLivingAdultsAtOrAboveTheThresholdCount()
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(
            Open(folder),
            Labs(PerPatient.Latest) with { FhirPath = $"entry.resource.where(value.ofType(Quantity).value >= 130 and {Living} and {Adult}).subject.reference" });

        envelope.Facts.ShouldNotBeNull().Value.ShouldNotBeNull().ToJsonString().ShouldBe("""["Patient/pe"]""");
        envelope.Issues.ShouldNotContain(issue => issue.Severity == "error");
    }

    [Fact]
    public void GivenColumnsCountAndOffset_WhenSearchedPerPatient_ThenTheyPageThroughTheKeptMatchesAndNextKeepsPerPatient()
    {
        using TestFolder folder = Cohort();
        SearchRequest request = Labs(PerPatient.Latest) with { Columns = [new("value", "value.ofType(Quantity).value")], Count = 1, Offset = 1 };

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), request);

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Rows.ShouldNotBeNull().ShouldHaveSingleItem().ToJsonString().ShouldBe("""["Observation/e2",135]""");
        facts.Total.ShouldBe(4);
        envelope.Omitted.ShouldBe([new OmittedItem("2 more matches (offset=2)", "limit")]);
        NextCall rest = envelope.Next.ShouldHaveSingleItem();
        rest.Args["perPatient"].ShouldBe(PerPatient.Latest);
        rest.Args["offset"].ShouldBe(2);
    }

    [Fact]
    public void GivenTheSmallestBudget_WhenSearchedPerPatientWithColumns_ThenTheResponseWithItsSelectionFactsStaysWithinIt()
    {
        using TestFolder folder = Cohort();
        SearchRequest request = Labs(PerPatient.Latest) with { Columns = [new("value", "value.ofType(Quantity).value")], MaxBytes = EnvelopeJson.MinBudget };

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), request);

        envelope.Facts.ShouldNotBeNull().PerPatient.ShouldNotBeNull();
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.MinBudget);
    }

    [Fact]
    public void GivenAPatientReferenceOutsideSubject_WhenSearchedPerPatient_ThenTheResourceIsAttributedByItsPatientCompartment()
    {
        using TestFolder folder = new TestFolder()
            .Write("patients.ndjson", """
                {"resourceType":"Patient","id":"pa"}
                {"resourceType":"Patient","id":"pb"}
                """)
            .Write("allergies.ndjson", """
                {"resourceType":"AllergyIntolerance","id":"x1","patient":{"reference":"Patient/pa"},"recordedDate":"2024-01-01","code":{"text":"peanut"}}
                {"resourceType":"AllergyIntolerance","id":"x2","patient":{"reference":"Patient/pa"},"recordedDate":"2025-01-01","code":{"text":"latex"}}
                {"resourceType":"AllergyIntolerance","id":"x3","patient":{"reference":"Patient/pb"},"recordedDate":"2023-01-01","code":{"text":"dust"}}
                """);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("AllergyIntolerance", "_sort=-date", PerPatient: PerPatient.First));

        Ids(envelope).ShouldBe(["x2", "x3"]);
        envelope.Facts.ShouldNotBeNull().PerPatient.ShouldBe(new PerPatientFacts(PerPatient.First, Sort: null, Matches: 3, Patients: 2, Kept: 2, WithoutPatient: 0));
    }

    [Fact]
    public void GivenAMatchInTwoPatientsCompartmentsAndOneInNone_WhenSearchedPerPatient_ThenTheFirstIsKeptOnceAndTheOtherIsCountedAsWithoutPatient()
    {
        using TestFolder folder = new TestFolder()
            .Write("patients.ndjson", """
                {"resourceType":"Patient","id":"pa"}
                {"resourceType":"Patient","id":"pb"}
                """)
            .Write("obs.ndjson", """
                {"resourceType":"Observation","id":"shared","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/pa"},"performer":[{"reference":"Patient/pb"}],"effectiveDateTime":"2025-01-01"}
                {"resourceType":"Observation","id":"later","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/pb"},"effectiveDateTime":"2024-01-01"}
                {"resourceType":"Observation","id":"nobody","status":"final","code":{"text":"x"},"effectiveDateTime":"2026-01-01"}
                """);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", PerPatient: PerPatient.Latest));

        Ids(envelope).ShouldBe(["shared"]);
        envelope.Facts.ShouldNotBeNull().PerPatient.ShouldBe(new PerPatientFacts(PerPatient.Latest, "-date", Matches: 3, Patients: 2, Kept: 1, WithoutPatient: 1));
    }

    // V14 lenient rule (ADR 2610); at V13 this failed asking to leave _sort out.
    [Theory]
    [InlineData(PerPatient.Latest, "_sort=-date")]
    [InlineData(PerPatient.Earliest, "_sort=value-quantity")]
    public void GivenADatePickWithASortOfItsOwn_WhenSearched_ThenTheSortIsDroppedWithAWarning(string pick, string sort)
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> sorted = ResourceSearcher.Search(Open(folder), Labs(pick, $"{Ldl}&{sort}"));
        ToolEnvelope<SearchFacts?> plain = ResourceSearcher.Search(Open(folder), Labs(pick));

        Ids(sorted).ShouldBe(Ids(plain));
        IssueItem adjusted = sorted.Issues[0];
        adjusted.Code.ShouldBe("argument-adjusted");
        adjusted.Diagnostics.ShouldContain($"perPatient={pick} orders the matches by date itself");
        sorted.Issues.Skip(1).ShouldBe(plain.Issues);
    }
}
