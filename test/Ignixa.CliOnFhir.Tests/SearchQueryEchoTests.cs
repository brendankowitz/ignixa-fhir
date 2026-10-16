// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Validation;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 9 fix round 3, response sizes with huge queries. Each test is a regression, failing at a5cace5: there the query was
/// repeated whole in facts and in <c>next</c>, outside every size budget, so a few hundred long parameters took a search,
/// plan or validation far past the 50 KB cap, and an executed plan halved its budget although the page left room.
/// </summary>
public class SearchQueryEchoTests
{
    private const int EchoLength = 2048;

    [Fact]
    public void GivenAThousandLenientParameters_WhenSearched_ThenTheResponseFitsAndTheQueryEchoIsCutWithItsDigest()
    {
        using TestFolder folder = Clinic();
        string query = Unknown(1000);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", query, Lenient: true));

        Size(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        JsonNode json = TerminologyFixture.Json(envelope);
        ((string)json["facts"]!["query"]!).Length.ShouldBeLessThanOrEqualTo(EchoLength + 1);
        Omitted(json).ShouldContain(what => what.StartsWith("facts.query past", StringComparison.Ordinal) && what.Contains($"{query.Length} characters", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenHundredsOfLongParameters_WhenPlanned_ThenTheResponseFitsAndNoNextRepeatsTheQuery(bool execute)
    {
        using TestFolder folder = Clinic();
        string query = Long(250);

        ToolEnvelope<PlanFacts?> plan = SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", query), execute);

        Size(plan).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        JsonNode json = TerminologyFixture.Json(plan);
        ((string)json["facts"]!["query"]!).Length.ShouldBeLessThanOrEqualTo(EchoLength + 1);
        (json["next"]?.AsArray() ?? []).ShouldBeEmpty();
        if (!execute)
        {
            Omitted(json).ShouldContain(what => what.StartsWith("next:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task GivenALongQueryWithMoreMatchesThanTheLimit_WhenValidated_ThenTheResponseFitsAndTheNextPageIsDescribedNotRepeated()
    {
        using TestFolder folder = Clinic();
        string query = string.Join('&', Enumerable.Repeat("status=final", 400));

        JsonNode json = TerminologyFixture.Json(await ResourceValidator.ValidateAsync(Open(folder), new ValidateRequest(ResourceType: "Observation", Query: query, Limit: 2)));

        System.Text.Encoding.UTF8.GetByteCount(json.ToJsonString()).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        ((string)json["facts"]!["scope"]!["query"]!).Length.ShouldBeLessThanOrEqualTo(EchoLength + 1);
        (json["next"]?.AsArray() ?? []).ShouldNotContain(call => call!.ToJsonString().Contains("status=final", StringComparison.Ordinal));
        Omitted(json).ShouldContain(what => what.StartsWith("next: the next page (offset 2)", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenSixtyRepeatedCodeParametersAndNoResults_WhenPlannedWithExecute_ThenAsManyParametersAreListedAsWithout()
    {
        // Many distinct codes, so each parameter's top values are large and the plan alone nearly fills the cap.
        using var folder = new TestFolder();
        folder.Write("obs.ndjson", string.Join('\n', Enumerable.Range(1, 300).Select(index => $$$"""{"resourceType":"Observation","id":"o{{{index}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"{{{index}}}-{{{index % 10}}}"}]}}""")));
        string query = string.Join('&', Enumerable.Repeat("code=http://loinc.org|0000-0", 60));

        JsonNode alone = TerminologyFixture.Json(SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", query)));
        ToolEnvelope<PlanFacts?> executed = SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", query), execute: true);

        Size(executed).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        JsonNode json = TerminologyFixture.Json(executed);
        ((int)json["facts"]!["results"]!["total"]!).ShouldBe(0);
        json["facts"]!["parameters"]!.AsArray().Count.ShouldBeGreaterThanOrEqualTo(alone["facts"]!["parameters"]!.AsArray().Count - 3);
    }

    private static string Unknown(int count) => string.Join('&', Enumerable.Range(1, count).Select(index => $"unknown-parameter-{index:0000}=some-value-{index}"));

    private static string Long(int count) => string.Join('&', Enumerable.Range(1, count).Select(index => $"code=http://example.org/{new string('x', 400)}|c{index}"));

    private static string[] Omitted(JsonNode json) => [.. json["omitted"]!.AsArray().Select(item => (string)item!["what"]!)];

    private static int Size<TFacts>(ToolEnvelope<TFacts> envelope) => JsonSerializer.SerializeToUtf8Bytes(envelope, EnvelopeJson.Compact).Length;
}
