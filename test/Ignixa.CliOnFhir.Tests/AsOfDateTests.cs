// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Drafting;
using Ignixa.CliOnFhir.Views;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// A date-only <c>--as-of</c> (the V14 merge's one option): <c>%asOf</c> is that day's first instant in UTC, as patient_summary and
/// patient_cohort read a date, and <c>%asOfDate</c> the date; a draft dates its dateTime members by the day and its instants by that
/// first instant. New behaviour: before the merge a date was refused as <c>invalid-as-of</c>.
/// </summary>
public class AsOfDateTests
{
    private const string AsOfColumns = """
        {"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"id","path":"id"},{"name":"as_of","path":"%asOf.toString()"},{"name":"as_of_date","path":"%asOfDate.toString()"}]}]}
        """;

    [Fact]
    public async Task GivenADateAsOf_WhenAViewReadsAsOfAndAsOfDate_ThenTheyAreTheDaysFirstInstantInUtcAndTheDay()
    {
        using TestFolder folder = SearchFixture.Clinic();

        ViewFacts facts = ViewFixture.Facts(await ViewFixture.RunAsync(SearchFixture.Open(folder), new ViewRequest(AsOfColumns), "2024-05-01"));

        ViewFixture.Cells(facts).Select(row => (row[1], row[2])).Distinct().ShouldBe([("2024-05-01T00:00:00Z", "2024-05-01")]);
        facts.AsOf.ShouldBe(new AsOfFacts("2024-05-01T00:00:00Z", AsOfSources.SessionOption, Date: new AsOfDateFacts("2024-05-01", AsOfSources.SessionOption), Precision: AsOf.DatePrecision));
    }

    [Fact]
    public async Task GivenAnInstantAsOf_WhenAViewRuns_ThenItsFactsCarryNoPrecision()
    {
        using TestFolder folder = SearchFixture.Clinic();

        ViewFacts facts = ViewFixture.Facts(await ViewFixture.RunAsync(SearchFixture.Open(folder), new ViewRequest(AsOfColumns), "2024-05-01T10:00:00+02:00"));

        facts.AsOf.Precision.ShouldBeNull();
        ViewFixture.Cells(facts).Select(row => (row[1], row[2])).Distinct().ShouldBe([("2024-05-01T10:00:00+02:00", "2024-05-01")]);
    }

    [Fact]
    public async Task GivenADateAsOf_WhenAMedicationRequestIsDrafted_ThenAuthoredOnIsTheDateNotAnInventedTime()
    {
        using TestFolder folder = DraftFixture.Clinic();

        DraftFacts facts = DraftFixture.Facts(await DraftFixture.RunAsync(folder, new DraftRequest("MedicationRequest", DraftFixture.PotassiumOrder), "2024-05-01"));

        DraftFixture.Text(facts.Resource["authoredOn"]).ShouldBe("2024-05-01");
        facts.AsOf.ShouldBe(AsOf.OfDate(new DateOnly(2024, 5, 1), AsOfSources.SessionOption));
        facts.AsOf.Precision.ShouldBe("date");
    }

    [Fact]
    public async Task GivenADateAsOf_WhenAnObservationIsDrafted_ThenEffectiveIsTheDateAndIssuedTheDaysFirstInstant()
    {
        using TestFolder folder = DraftFixture.Clinic();

        DraftFacts facts = DraftFixture.Facts(await DraftFixture.RunAsync(folder, new DraftRequest("Observation", DraftFixture.PotassiumResult), "2024-05-01"));

        DraftFixture.Text(facts.Resource["effectiveDateTime"]).ShouldBe("2024-05-01");
        DraftFixture.Text(facts.Resource["issued"]).ShouldBe("2024-05-01T00:00:00Z");
    }
}
