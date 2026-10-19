// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b fix round 1: where relative dates are read (I8: the end of a <c>_has</c> chain, a composite's date components), how
/// <c>today</c> is searched when asOf has an offset (M1: that day's bounds in UTC, not the date read as a UTC day), the README's
/// examples run against the engine (M2), and the note's wording (M10). Regressions: at 86a9d902 a relative date under <c>_has</c>
/// or in a composite reached the engine as text and failed there, <c>today</c> in an offset searched the UTC day of its label,
/// and the README's <c>date=genow-7d,lttoday-30d</c> failed in the engine.
/// </summary>
public class RelativeDateScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    // p1 has heart rates 1 and 10 days back; p2 one 40 days back. v1 and v2 hold a dateTime value, 3 and 45 days back.
    // t1 to t4, of a patient the folder does not hold, are around the day 2026-10-15 at -05:00, which is 2026-10-15T05:00:00Z to
    // 2026-10-16T05:00:00Z.
    private static TestFolder Chart() => new TestFolder().Write("data.ndjson", string.Join('\n',
        [
            """{"resourceType":"Patient","id":"p1","birthDate":"1980-06-01"}""",
            """{"resourceType":"Patient","id":"p2","birthDate":"1950-06-01"}""",
            Reading("o1", "p1", Now.AddDays(-1)),
            Reading("o10", "p1", Now.AddDays(-10)),
            Reading("o40", "p2", Now.AddDays(-40)),
            Valued("v1", Now.AddDays(-3)),
            Valued("v2", Now.AddDays(-45)),
            Reading("t1", "p3", new DateTimeOffset(2026, 10, 15, 4, 0, 0, TimeSpan.Zero)),
            Reading("t2", "p3", new DateTimeOffset(2026, 10, 15, 6, 0, 0, TimeSpan.Zero)),
            Reading("t3", "p3", new DateTimeOffset(2026, 10, 16, 4, 0, 0, TimeSpan.Zero)),
            Reading("t4", "p3", new DateTimeOffset(2026, 10, 16, 6, 0, 0, TimeSpan.Zero)),
        ]));

    private static string Instant(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Reading(string id, string patient, DateTimeOffset at) =>
        $$$"""{"resourceType":"Observation","id":"{{{id}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/{{{patient}}}"},"effectiveDateTime":"{{{Instant(at)}}}","valueQuantity":{"value":70,"unit":"/min"}}""";

    private static string Valued(string id, DateTimeOffset value) =>
        $$$"""{"resourceType":"Observation","id":"{{{id}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"21112-8"}]},"subject":{"reference":"Patient/p1"},"valueDateTime":"{{{Instant(value)}}}"}""";

    private static FolderSession Open(TestFolder folder, string? asOf = null) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: new ManualTimeProvider { Now = Now }, asOfOption: asOf);

    private static string[] Ids(ToolEnvelope<SearchFacts?> envelope) => [.. SearchFixture.Ids(envelope).Order(StringComparer.Ordinal)];

    private static string Resolved(FolderSession session, string type, string query, AsOf asOf) =>
        RelativeDates.Resolve(session.Definitions, new SearchRequest(type, query), () => asOf).Request.Query!;

    [Fact]
    public void GivenARelativeDateAtTheEndOfAHasChain_WhenSearched_ThenItIsResolvedAndMatches()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_has:Observation:patient:date=genow-7d"));

        envelope.Issues.ShouldNotContain(issue => issue.Severity == "error");
        Ids(envelope).ShouldBe(["p1"]);
        envelope.Facts.ShouldNotBeNull().Query.ShouldBe("_has:Observation:patient:date=ge2026-10-08T12:00:00Z");
    }

    [Theory]
    [InlineData("Patient", "_has:Observation:patient:_has:Provenance:target:recorded=genow-1d", "_has:Observation:patient:_has:Provenance:target:recorded=ge2026-10-14T12:00:00Z")]
    [InlineData("Patient", "_has:Observation:subject:date=lttoday-1y", "_has:Observation:subject:date=lt2025-10-15")]
    [InlineData("Observation", "code-value-date=http://loinc.org|21112-8$genow-7d", "code-value-date=http://loinc.org|21112-8$ge2026-10-08T12:00:00Z")]
    [InlineData("Observation", "code-value-date=http://loinc.org|21112-8$genow-7d,http://loinc.org|21112-8$lttoday", "code-value-date=http://loinc.org|21112-8$ge2026-10-08T12:00:00Z,http://loinc.org|21112-8$lt2026-10-15")]
    public void GivenARelativeDateInAHasChainOrACompositesDateComponent_WhenResolved_ThenItIsTheAbsoluteValue(string type, string query, string expected)
    {
        using TestFolder folder = Chart();

        Resolved(Open(folder), type, query, new AsOf(Now, AsOfSources.Clock)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Patient", "_has:Observation:patient:code=now")]
    [InlineData("Observation", "code-value-string=http://loinc.org|1234-5$now")]
    [InlineData("Observation", "code-value-date=now$ge2020")]
    public void GivenNowOrTodayOutsideADateValue_WhenResolved_ThenTheQueryIsUntouchedAndAsOfIsNotAsked(string type, string query)
    {
        using TestFolder folder = Chart();

        RelativeDates.Resolve(Open(folder).Definitions, new SearchRequest(type, query), () => throw new InvalidOperationException("asOf was asked for"))
            .Request.Query.ShouldBe(query);
    }

    [Fact]
    public void GivenARelativeDateInACompositesDateComponent_WhenSearched_ThenItMatchesThoseWhoseValueIsInRange()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "code-value-date=http://loinc.org|21112-8$genow-7d"));

        envelope.Issues.ShouldNotContain(issue => issue.Severity == "error");
        Ids(envelope).ShouldBe(["v1"]);
    }

    [Fact]
    public void GivenAnAsOfWithAnOffset_WhenTodayIsSearched_ThenTheMatchesAreThoseOfThatLocalDay()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, "2026-10-15T23:30:00-05:00"), new SearchRequest("Observation", "date=today&code=8867-4"));

        Ids(envelope).ShouldBe(["t2", "t3"]);
        envelope.Facts.ShouldNotBeNull().Query.ShouldBe("date=sa2026-10-15T04:59:59Z&date=eb2026-10-16T05:00:00Z&code=8867-4");
        envelope.Issues.Single(issue => issue.Code == RelativeDates.Code).Diagnostics.ShouldContain("in its offset -05:00, written as that day's bounds in UTC");
    }

    [Theory]
    [InlineData("date=getoday", "date=ge2026-10-15T05:00:00Z")]
    [InlineData("date=lttoday", "date=lt2026-10-15T05:00:00Z")]
    [InlineData("date=gttoday", "date=ge2026-10-16T05:00:00Z")]
    [InlineData("date=letoday", "date=lt2026-10-16T05:00:00Z")]
    [InlineData("date=ebtoday", "date=eb2026-10-15T05:00:00Z")]
    [InlineData("date=satoday", "date=sa2026-10-16T04:59:59Z")]
    [InlineData("date=eqtoday-1d", "date=sa2026-10-14T04:59:59Z&date=eb2026-10-15T05:00:00Z")]
    [InlineData("date=getoday-1d,lt2000", "date=ge2026-10-14T05:00:00Z,lt2000")]
    public void GivenAnAsOfWithAnOffset_WhenTodayIsResolved_ThenItIsThatLocalDaysBoundsInUtc(string query, string expected)
    {
        using TestFolder folder = Chart();

        Resolved(Open(folder), "Observation", query, new AsOf(DateTimeOffset.Parse("2026-10-15T23:30:00-05:00", CultureInfo.InvariantCulture), AsOfSources.SessionOption)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("date=netoday")]
    [InlineData("date=aptoday")]
    [InlineData("date=today,2020")]
    public void GivenAnAsOfWithAnOffset_WhenATodayThatNeedsBothBoundsCannotHaveThem_ThenTheCallFailsNamingTheBounds(string query)
    {
        using TestFolder folder = Chart();

        IssueItem issue = ResourceSearcher.Search(Open(folder, "2026-10-15T23:30:00-05:00"), new SearchRequest("Observation", query)).Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain("write date=sa2026-10-15T04:59:59Z&date=eb2026-10-16T05:00:00Z (within the day)");
    }

    [Fact]
    public void GivenEveryRelativeDateExampleOfTheReadme_WhenSearched_ThenTheEngineAcceptsEach()
    {
        using TestFolder folder = Chart();
        FolderSession session = Open(folder);

        List<string> refused = [];
        foreach (string example in ReadmeExamples())
        {
            string type = example.StartsWith("birthdate", StringComparison.Ordinal) || example.StartsWith("_has:", StringComparison.Ordinal) ? "Patient" : "Observation";
            ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(session, new SearchRequest(type, example));
            if (envelope.Facts is null || envelope.Issues.Any(issue => issue.Severity == "error"))
            {
                refused.Add($"{type}?{example}: {string.Join("; ", envelope.Issues.Select(issue => $"{issue.Code} {issue.Diagnostics}"))}");
            }
        }

        refused.ShouldBeEmpty();
    }

    [Fact]
    public void GivenOneResolvedParameter_WhenPlanned_ThenTheNoteSaysOneWasResolvedWithoutClaimingASearchRan()
    {
        using TestFolder folder = Chart();

        IssueItem note = SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", "date=genow-7d")).Issues.Single(issue => issue.Code == RelativeDates.Code);

        note.Diagnostics.ShouldStartWith("1 parameter with relative dates was resolved against asOf");
        note.Diagnostics.ShouldNotContain("before the search ran");
    }

    // The query examples of the README's relative-dates section: each backticked name=value that has now or today in its value.
    private static List<string> ReadmeExamples()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "Ignixa.CliOnFhir", "README.md"));
        int start = readme.IndexOf("## Relative dates", StringComparison.Ordinal);
        string section = readme[start..readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal)];
        List<string> examples = [.. Regex.Matches(section, "`([^`\\s]+=[^`\\s]*(?:now|today)[^`\\s]*)`").Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)];
        examples.Count.ShouldBeGreaterThanOrEqualTo(5);
        return [.. examples.Where(example => !example.Contains("missing", StringComparison.Ordinal) && !example.StartsWith("code=", StringComparison.Ordinal))];
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "All.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root (All.sln) was not found above the test binaries.");
    }
}
