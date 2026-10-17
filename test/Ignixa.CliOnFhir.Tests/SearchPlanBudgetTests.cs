// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>plan_fhir_search</c> fix round 1. Each test is a regression, failing at e3eeee2: there a plan of many parameters
/// exceeded the 50 KB cap with nothing in <c>omitted</c>, a token parameter whose values have a system but no code was
/// reported as unable to match, and the plan skipped the search's paging checks and dropped its paging arguments from
/// <c>next</c>. The tests read the envelope as JSON, so they compile there.
/// </summary>
public class SearchPlanBudgetTests
{
    [Theory]
    [InlineData(60, false)]
    [InlineData(300, false)]
    [InlineData(300, true)]
    public void GivenAQueryWithManyParameters_WhenPlanned_ThenTheResponseStaysUnderTheCapAndSaysWhatWasLeftOut(int parameters, bool execute)
    {
        using TestFolder folder = Clinic();
        string query = string.Join('&', Enumerable.Repeat("code=http://loinc.org|8867-4", parameters));

        ToolEnvelope<PlanFacts?> plan = SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", query), execute);

        int size = JsonSerializer.SerializeToUtf8Bytes(plan, EnvelopeJson.Compact).Length;
        size.ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        JsonNode json = TerminologyFixture.Json(plan);
        json["omitted"]!.AsArray().ShouldContain(item => (string)item!["why"]! == "size-budget");
        if (execute)
        {
            ((int)json["facts"]!["results"]!["total"]!).ShouldBe(4);
        }
    }

    [Fact]
    public void GivenTokenValuesWithASystemButNoCode_WhenPlannedWithSystemBar_ThenTheyAreTopValuesAndNoUnpopulatedWarningIsRaised()
    {
        using var folder = new TestFolder();
        folder.Write("obs.ndjson", """{"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://example.org/codes"}]}}""");

        JsonNode plan = TerminologyFixture.Json(SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", "code=http://example.org/codes|")));

        plan["issues"]!.AsArray().ShouldNotContain(issue => (string)issue!["code"]! == "parameter-unpopulated");
        ((string)plan["facts"]!["parameters"]![0]!["resolves"]![0]!["topValues"]![0]!["value"]!).ShouldBe("http://example.org/codes|");
    }

    [Fact]
    public void GivenPagingArguments_WhenPlanned_ThenThePlanChecksThemAsTheSearchDoesAndKeepsThemInNext()
    {
        using TestFolder folder = Clinic();
        FolderSession session = Open(folder);

        JsonNode zero = TerminologyFixture.Json(SearchPlanner.Plan(session, new SearchRequest("Patient", "family=Smith", Count: 0)));
        JsonNode search = TerminologyFixture.Json(ResourceSearcher.Search(session, new SearchRequest("Patient", "family=Smith", Count: 0)));
        JsonNode large = TerminologyFixture.Json(SearchPlanner.Plan(session, new SearchRequest("Patient", "family=Smith", Count: 99, Offset: 1, Elements: "name")));

        zero["facts"].ShouldBeNull();
        zero["issues"]!.ToJsonString().ShouldBe(search["issues"]!.ToJsonString());
        large["issues"]!.AsArray().ShouldContain(issue => (string)issue!["code"]! == "count-clamped");
        JsonNode args = large["next"]![0]!["args"]!;
        ((int)args["count"]!, (int)args["offset"]!, (string)args["elements"]!).ShouldBe((99, 1, "name"));
    }
}
