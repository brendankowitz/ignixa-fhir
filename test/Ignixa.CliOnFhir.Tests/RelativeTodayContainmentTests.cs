// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b fix round 2, Q2: a bare or <c>eq</c> <c>today</c> with an asOf in an offset other than UTC means a value within that
/// local day, as a FHIR date does: <c>sa</c> the second before the day and <c>eb</c> the next day's start, on a plain parameter.
/// Under <c>_has</c> or a chain the two clauses would hold independently, each perhaps of another resource, so it is refused.
/// Regressions: at dc1b5418 it was written as overlap (<c>ge</c> start and <c>lt</c> end), so a Period spanning the day matched,
/// and under <c>_has</c> a patient with one observation before the day and one after it matched (the reviewer's p1/p2 repro).
/// </summary>
public class RelativeTodayContainmentTests
{
    // asOf 2026-10-15T23:30:00-05:00: the local day is 2026-10-15T05:00:00Z to 2026-10-16T05:00:00Z.
    private const string AsOf = "2026-10-15T23:30:00-05:00";

    // p1: one observation within the day. p2: one the hour before the day starts and one the hour after it ends. s1 spans the day.
    private static TestFolder Chart() => new TestFolder().Write("data.ndjson", """
        {"resourceType":"Patient","id":"p1","birthDate":"1980-06-01"}
        {"resourceType":"Patient","id":"p2","birthDate":"1980-06-01"}
        {"resourceType":"Observation","id":"in","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"2026-10-15T12:00:00Z"}
        {"resourceType":"Observation","id":"before","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p2"},"effectiveDateTime":"2026-10-15T04:00:00Z"}
        {"resourceType":"Observation","id":"after","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p2"},"effectiveDateTime":"2026-10-16T06:00:00Z"}
        {"resourceType":"Observation","id":"s1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"},"effectivePeriod":{"start":"2026-10-14T00:00:00Z","end":"2026-10-17T00:00:00Z"}}
        """);

    private static FolderSession Open(TestFolder folder) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, asOfOption: AsOf);

    [Theory]
    [InlineData("date=today")]
    [InlineData("date=eqtoday")]
    public void GivenAnOffsetAsOf_WhenABareTodayIsSearched_ThenOnlyValuesWithinTheLocalDayMatch(string query)
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", query));

        SearchFixture.Ids(envelope).ShouldBe(["in"]);
        envelope.Facts!.Query.ShouldBe("date=sa2026-10-15T04:59:59Z&date=eb2026-10-16T05:00:00Z");
    }

    [Theory]
    [InlineData("Patient", "_has:Observation:patient:date=today")]
    [InlineData("Patient", "_has:Observation:subject:date=eqtoday")]
    [InlineData("Observation", "subject:Patient.birthdate=today")]
    public void GivenAnOffsetAsOf_WhenABareTodayIsUnderHasOrAChain_ThenItIsRefusedNamingTheRemedy(string type, string query)
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest(type, query));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain("give an asOf in UTC, or explicit ge and lt bounds");
    }

    [Fact]
    public void GivenAnOffsetAsOf_WhenATodayWithOneBoundIsUnderHas_ThenItIsStillResolved()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_has:Observation:patient:date=getoday"));

        envelope.Facts.ShouldNotBeNull().Query.ShouldBe("_has:Observation:patient:date=ge2026-10-15T05:00:00Z");
    }
}
