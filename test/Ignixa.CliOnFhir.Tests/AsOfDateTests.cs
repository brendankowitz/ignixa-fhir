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
/// first instant; the forms V14 accepted (a year, a month, a time without a zone) read the same way. New behaviour: before the
/// merge only an instant was accepted.
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

    [Theory]
    [InlineData("2024", "2024-01-01T00:00:00Z", "2024-01-01", "year", null)]
    [InlineData("2024-05", "2024-05-01T00:00:00Z", "2024-05-01", "month", null)]
    [InlineData("2024-05-01T08:30:00", "2024-05-01T08:30:00Z", "2024-05-01", null, true)]
    public async Task GivenAnAsOfInAFormV14Accepted_WhenAViewReadsIt_ThenAsOfIsTheResolvedInstantAndAsOfDateItsDay(string asOf, string instant, string day, string? precision, bool? zoneAssumed)
    {
        using TestFolder folder = SearchFixture.Clinic();

        ViewFacts facts = ViewFixture.Facts(await ViewFixture.RunAsync(SearchFixture.Open(folder), new ViewRequest(AsOfColumns), asOf));

        ViewFixture.Cells(facts).Select(row => (row[1], row[2])).Distinct().ShouldBe([(instant, day)]);
        facts.AsOf.Precision.ShouldBe(precision);
        facts.AsOf.ZoneAssumed.ShouldBe(zoneAssumed);
    }

    [Theory]
    [InlineData("2024", "2024", "2024-01-01T00:00:00Z")]
    [InlineData("2024-05", "2024-05", "2024-05-01T00:00:00Z")]
    [InlineData("2024-05-01T08:30:00", "2024-05-01T08:30:00Z", "2024-05-01T08:30:00Z")]
    public async Task GivenAnAsOfInAFormV14Accepted_WhenAnObservationIsDrafted_ThenEffectiveKeepsItsPrecisionAndIssuedIsAnInstant(string asOf, string effective, string issued)
    {
        using TestFolder folder = DraftFixture.Clinic();

        DraftFacts facts = DraftFixture.Facts(await DraftFixture.RunAsync(folder, new DraftRequest("Observation", DraftFixture.PotassiumResult), asOf));

        DraftFixture.Text(facts.Resource["effectiveDateTime"]).ShouldBe(effective);
        DraftFixture.Text(facts.Resource["issued"]).ShouldBe(issued);
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
