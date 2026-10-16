// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 9 fix round 2, search and plan sizes. Each test is a regression, failing at 29aa44d: there every lenient
/// <c>parameter-ignored</c> warning was listed, so a few hundred unknown parameters exceeded the 50 KB cap, and an executed
/// plan was fitted to half the cap whatever the page needed, leaving parameters out with space to spare.
/// </summary>
public class SearchIgnoredCapTests
{
    private const int Unknown = 250;

    private const int Listed = 20;

    [Fact]
    public void GivenHundredsOfUnknownParametersInLenientMode_WhenSearched_ThenTwentyWarningsAreListedTheRestCountedAndTheResponseFits()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", UnknownParameters(), Lenient: true));

        Size(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        JsonNode json = TerminologyFixture.Json(envelope);
        Ignored(json).ShouldBe(Listed);
        json["omitted"]!.AsArray().ShouldContain(item => (string)item!["why"]! == "size-budget" && ((string)item["what"]!).StartsWith($"{Unknown - Listed} more parameter-ignored", StringComparison.Ordinal));
        ((int)json["facts"]!["total"]!).ShouldBe(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenHundredsOfUnknownParametersInLenientMode_WhenPlanned_ThenTheResponseFitsWithTheWarningsCapped(bool execute)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<PlanFacts?> plan = SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", UnknownParameters(), Lenient: true), execute);

        Size(plan).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        JsonNode json = TerminologyFixture.Json(plan);
        Ignored(json).ShouldBeLessThanOrEqualTo(Listed);
        json["omitted"]!.AsArray().ShouldContain(item => (string)item!["why"]! == "size-budget");
        if (execute)
        {
            ((int)json["facts"]!["results"]!["total"]!).ShouldBe(5);
        }
    }

    [Fact]
    public void GivenSixtyParametersAndNoResults_WhenPlannedWithExecute_ThenEveryParameterIsListedAndNothingIsLeftOut()
    {
        using TestFolder folder = Clinic();
        string query = string.Join('&', Enumerable.Range(1, 60).Select(index => $"status=cancelled{index}"));

        ToolEnvelope<PlanFacts?> plan = SearchPlanner.Plan(Open(folder), new SearchRequest("Observation", query), execute: true);

        Size(plan).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        JsonNode json = TerminologyFixture.Json(plan);
        ((int)json["facts"]!["results"]!["total"]!).ShouldBe(0);
        json["facts"]!["parameters"]!.AsArray().Count.ShouldBe(60);
        json["omitted"]!.AsArray().ShouldNotContain(item => (string)item!["why"]! == "size-budget");
    }

    private static string UnknownParameters() => string.Join('&', Enumerable.Range(1, Unknown).Select(index => $"unknown-parameter-{index:000}=some-value-{index}"));

    private static int Ignored(JsonNode json) => json["issues"]!.AsArray().Count(issue => (string)issue!["code"]! == "parameter-ignored");

    private static int Size<TFacts>(ToolEnvelope<TFacts> envelope) => JsonSerializer.SerializeToUtf8Bytes(envelope, EnvelopeJson.Compact).Length;
}
