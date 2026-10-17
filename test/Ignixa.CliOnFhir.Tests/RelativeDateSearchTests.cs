// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b, item 3: relative dates of date search parameters (<c>date=genow-7d</c>) resolved against asOf before the search
/// parses the query, stated in facts and an information note. New behaviour: before it <c>now</c> was not a date the search read.
/// </summary>
public class RelativeDateSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly int[] DaysBack = [1, 3, 10, 40, 400];

    // Heart rates 1, 3, 10, 40 and 400 days before Now, and a patient born in 1980.
    private static TestFolder Readings() => new TestFolder().Write("data.ndjson", string.Join('\n',
        [
            """{"resourceType":"Patient","id":"p1","birthDate":"1980-06-01"}""",
            .. DaysBack.Select(days =>
                $$$"""{"resourceType":"Observation","id":"o{{{days}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"{{{Now.AddDays(-days).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}}}"}"""),
        ]));

    private static FolderSession Open(TestFolder folder, string? asOf = null) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: new ManualTimeProvider { Now = Now }, asOfOption: asOf);

    private static string[] Ids(ToolEnvelope<SearchFacts?> envelope) => SearchFixture.Ids(envelope).Order(StringComparer.Ordinal).ToArray();

    private static string Resolved(FolderSession session, string query, AsOf asOf) =>
        RelativeDates.Resolve(session.Definitions, new SearchRequest("Observation", query), () => asOf).Request.Query!;

    [Theory]
    [InlineData("date=genow", "date=ge2026-10-15T12:00:00Z")]
    [InlineData("date=genow-24h", "date=ge2026-10-14T12:00:00Z")]
    [InlineData("date=gtnow-7d", "date=gt2026-10-08T12:00:00Z")]
    [InlineData("date=lenow-2wk", "date=le2026-10-01T12:00:00Z")]
    [InlineData("date=ltnow-3mo", "date=lt2026-07-15T12:00:00Z")]
    [InlineData("date=sanow-1y", "date=sa2025-10-15T12:00:00Z")]
    [InlineData("date=today", "date=2026-10-15")]
    [InlineData("date=eqtoday-2d", "date=eq2026-10-13")]
    [InlineData("date=netoday-1wk", "date=ne2026-10-08")]
    [InlineData("date=aptoday-1mo", "date=ap2026-09-15")]
    [InlineData("date=ebtoday-10y", "date=eb2016-10-15")]
    [InlineData("date=genow-7d,lttoday-30d&code=8867-4", "date=ge2026-10-08T12:00:00Z,lt2026-09-15&code=8867-4")]
    public void GivenARelativeDateOfEachUnitAndPrefix_WhenResolved_ThenItIsTheAbsoluteValueCountedBackFromAsOf(string query, string expected)
    {
        using TestFolder folder = Readings();

        Resolved(Open(folder), query, new AsOf(Now, AsOfSources.Clock)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2025-03-31T10:00:00Z", "date=genow-1mo", "date=ge2025-02-28T10:00:00Z")]
    [InlineData("2024-03-31T10:00:00Z", "date=genow-1mo", "date=ge2024-02-29T10:00:00Z")]
    [InlineData("2024-02-29T10:00:00Z", "date=genow-1y", "date=ge2023-02-28T10:00:00Z")]
    [InlineData("2025-03-31T10:00:00Z", "date=getoday-1mo", "date=ge2025-02-28")]
    public void GivenAMonthOrYearBackFromADayTheTargetLacks_WhenResolved_ThenItIsTheLastDayOfThatMonth(string asOf, string query, string expected)
    {
        using TestFolder folder = Readings();

        Resolved(Open(folder), query, new AsOf(DateTimeOffset.Parse(asOf, CultureInfo.InvariantCulture), AsOfSources.SessionOption)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-10-15T23:30:00Z", "date=today", "date=2026-10-15")]
    [InlineData("2026-10-15T23:30:00-05:00", "date=today", "date=2026-10-15")]
    [InlineData("2026-10-15T23:30:00-05:00", "date=genow", "date=ge2026-10-16T04:30:00Z")]
    [InlineData("2026-10-16T01:00:00+05:30", "date=today", "date=2026-10-16")]
    public void GivenAnAsOfNearMidnightWithOrWithoutAnOffset_WhenTodayAndNowAreResolved_ThenTodayIsItsOwnDayAndNowIsInUtc(string asOf, string query, string expected)
    {
        using TestFolder folder = Readings();

        Resolved(Open(folder), query, new AsOf(DateTimeOffset.Parse(asOf, CultureInfo.InvariantCulture), AsOfSources.SessionOption)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("date:missing=true")]
    [InlineData("code=now")]
    [InlineData("_id=today")]
    [InlineData("date=ge2026-01-01")]
    public void GivenNoRelativeDateOfADateParameter_WhenResolved_ThenTheQueryIsUntouchedAndAsOfIsNotAsked(string query)
    {
        using TestFolder folder = Readings();

        (SearchRequest request, IReadOnlyList<ResolvedParameter> resolved, IssueItem? note) = RelativeDates.Resolve(Open(folder).Definitions, new SearchRequest("Observation", query), () => throw new InvalidOperationException("asOf was asked for"));

        request.Query.ShouldBe(query);
        resolved.ShouldBeEmpty();
        note.ShouldBeNull();
    }

    [Theory]
    [InlineData("date=genow-3m", "now or today, optionally less")]
    [InlineData("date=getoday-24h", "hours count back from now")]
    [InlineData("date=nowish", "now or today, optionally less")]
    [InlineData("date=genow-9999999y", "now or today, optionally less")]
    [InlineData("date=genow-999999y", "before the year 1")]
    public void GivenAValueThatStartsLikeARelativeDateButIsNotOne_WhenSearched_ThenTheCallFailsAsInvalidParameter(string query, string reason)
    {
        using TestFolder folder = Readings();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", query));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain(reason);
    }

    [Fact]
    public void GivenARelativeDate_WhenSearched_ThenTheMatchesAreThoseOfTheResolvedQueryAndFactsSayHow()
    {
        using TestFolder folder = Readings();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "date=genow-7d&code=8867-4"));

        Ids(envelope).ShouldBe(["o1", "o3"]);
        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Query.ShouldBe("date=ge2026-10-08T12:00:00Z&code=8867-4");
        facts.OriginalQuery.ShouldBe("date=genow-7d&code=8867-4");
        facts.ResolvedParameters.ShouldBe([new ResolvedParameter("date", "genow-7d", "ge2026-10-08T12:00:00Z")]);
        IssueItem note = envelope.Issues.ShouldHaveSingleItem();
        note.Code.ShouldBe(RelativeDates.Code);
        note.Severity.ShouldBe("information");
        note.Diagnostics.ShouldContain("asOf 2026-10-15T12:00:00Z (clock)");
        note.Diagnostics.ShouldContain("today as the asOf day in UTC");
    }

    [Fact]
    public void GivenAnAbsoluteQuery_WhenSearched_ThenFactsCarryNoRewrite()
    {
        using TestFolder folder = Readings();

        SearchFacts facts = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "date=ge2026-10-08")).Facts.ShouldNotBeNull();

        facts.OriginalQuery.ShouldBeNull();
        facts.ResolvedParameters.ShouldBeNull();
    }

    [Fact]
    public void GivenTheSessionOptionAndACallAsOf_WhenSearched_ThenTheCallsCountsAndTheEnvelopeSaysSo()
    {
        using TestFolder folder = Readings();
        FolderSession session = Open(folder, "2026-09-01");

        ToolEnvelope<SearchFacts?> bySession = ResourceSearcher.Search(session, new SearchRequest("Observation", "date=genow-30d"));
        ToolEnvelope<SearchFacts?> byCall = ResourceSearcher.Search(session, new SearchRequest("Observation", "date=genow-30d", AsOf: "2026-10-15T00:00:00Z"));

        Ids(bySession).ShouldBe(["o1", "o10", "o3", "o40"]);
        bySession.AsOf.ShouldNotBeNull().Source.ShouldBe(AsOfSources.SessionOption);
        Ids(byCall).ShouldBe(["o1", "o10", "o3"]);
        byCall.AsOf.ShouldBe(new AsOf(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), AsOfSources.Argument));
    }

    [Fact]
    public void GivenAChainToADateParameter_WhenSearched_ThenTheChainsValueIsResolved()
    {
        using TestFolder folder = Readings();

        SearchFacts facts = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "subject:Patient.birthdate=lenow-40y&date=genow-2d")).Facts.ShouldNotBeNull();

        facts.Total.ShouldBe(1);
        facts.ResolvedParameters.ShouldNotBeNull().Select(parameter => parameter.Parameter).ShouldBe(["subject:Patient.birthdate", "date"]);
    }

    [Fact]
    public void GivenAMalformedCallAsOf_WhenSearched_ThenTheCallFailsAsInvalidAsOf()
    {
        using TestFolder folder = Readings();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "date=genow-7d", AsOf: "soon"));

        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe(AsOfClock.InvalidCode);
    }

    [Fact]
    public void GivenARelativeDate_WhenPlanned_ThenThePlanShowsTheQueryAsSearchedAndAsWritten()
    {
        using TestFolder folder = Readings();

        ToolEnvelope<PlanFacts?> envelope = SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", "date=genow-7d"));

        PlanFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Query.ShouldBe("date=ge2026-10-08T12:00:00Z");
        facts.OriginalQuery.ShouldBe("date=genow-7d");
        facts.ResolvedParameters.ShouldNotBeNull().ShouldHaveSingleItem().Resolved.ShouldBe("ge2026-10-08T12:00:00Z");
        envelope.Issues.ShouldContain(issue => issue.Code == RelativeDates.Code);
    }

    [Fact]
    public void GivenARelativeDateAndAnAsOfOverMcp_WhenSearched_ThenTheAnswerCountsFromTheCallsAsOf()
    {
        using TestFolder folder = Readings();
        var tools = new ResourceTools(Open(folder), McpToolset.Query);

        JsonNode envelope = JsonNode.Parse(tools.SearchFhirResources("Observation", "date=genow-2d", asOf: "2026-10-12T12:00:00Z").Content.OfType<TextContentBlock>().Single().Text)!;

        ((int)envelope["facts"]!["total"]!).ShouldBe(2);
        ((string)envelope["facts"]!["resolvedParameters"]![0]!["resolved"]!).ShouldBe("ge2026-10-10T12:00:00Z");
        ((string)envelope["asOf"]!["source"]!).ShouldBe(AsOfSources.Argument);
    }
}
