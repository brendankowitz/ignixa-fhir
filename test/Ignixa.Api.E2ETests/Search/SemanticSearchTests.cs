// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.Api.E2ETests.Search;

/// <summary>
/// E2E coverage for semantic (vector) search: a <c>special</c> SearchParameter carrying the
/// <c>vector-search-config</c> extension, activated through the same package-activation path real
/// deployments use (see <see cref="SemanticSearchApiFixture"/>), searched with the test-only
/// deterministic embedding generator so ranking is reproducible without a live provider.
/// </summary>
/// <remarks>
/// Every test shares <see cref="SemanticSearchApiFixture"/>'s one host/database (see
/// <see cref="SemanticSearchTestCollection"/>) and isolates its own data with a per-test <c>_tag</c>, so
/// tests may run in any order without seeing each other's Observations. The feature-disabled behavior
/// (Review Focus 5 in the slice-1 plan) is pinned separately in <see cref="SemanticSearchDisabledTests"/>,
/// which needs VectorSearch to stay off and therefore cannot share this fixture.
/// </remarks>
[Collection(SemanticSearchTestCollection.Name)]
public class SemanticSearchTests : CapabilityDrivenTestBase
{
    private readonly SemanticSearchApiFixture _fixture;

    public SemanticSearchTests(SemanticSearchApiFixture fixture) : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenActivatedSemanticParam_WhenObservationsCreatedAndSearched_ThenRankedBundleWithScores()
    {
        await _fixture.EnsureSemanticSearchParameterActivatedAsync();
        var tag = Guid.NewGuid().ToString("N");
        const string targetText = "patient reports severe chest pain radiating to the left arm";

        var target = await CreateObservationAsync(tag, targetText);
        await CreateObservationAsync(tag, "routine wellness visit with no complaints today");
        await CreateObservationAsync(tag, "mild seasonal allergy symptoms reported this week");

        using var response = await SemanticSearchAsync(tag, targetText);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var matches = MatchEntries(body);
        matches.Length.ShouldBe(3);

        var scores = matches.Select(ReadScore).ToArray();
        foreach (var score in scores)
        {
            score.ShouldBeGreaterThanOrEqualTo(0);
            score.ShouldBeLessThanOrEqualTo(1);
        }

        scores.ShouldBe(scores.OrderDescending().ToArray(), "Matches must be ranked by descending score.");
        ReadId(matches[0]).ShouldBe(target.Id, "The Observation whose text exactly matches the query must rank first.");
        scores[0].ShouldBeGreaterThan(0.99, "An exact-text query against its own indexed passage should score close to 1.0.");
    }

    [Fact]
    public async Task GivenSemanticAndStatus_WhenStrictSearch_ThenOnlyMatchingStatusReturned()
    {
        await _fixture.EnsureSemanticSearchParameterActivatedAsync();
        var tag = Guid.NewGuid().ToString("N");
        const string text = "elevated blood pressure reading noted during the visit";

        var final = await CreateObservationAsync(tag, text, status: "final");
        await CreateObservationAsync(tag, text, status: "preliminary");

        using var response = await SemanticSearchAsync(tag, text, extraQuery: "&status=final");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var matches = MatchEntries(body);
        matches.Length.ShouldBe(1);
        ReadId(matches[0]).ShouldBe(final.Id);
    }

    [Fact]
    public async Task GivenEnabled_WhenMetadataRequested_ThenSemanticParamAdvertisedAsSpecial()
    {
        await _fixture.EnsureSemanticSearchParameterActivatedAsync();

        using var response = await Client.GetAsync("/metadata");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var resources = JsonNode.Parse(body)!["rest"]!.AsArray()[0]!["resource"]!.AsArray();
        var observationResource = resources.Single(resource => resource!["type"]!.GetValue<string>() == "Observation");
        var searchParam = observationResource!["searchParam"]!.AsArray()
            .Single(parameter => parameter!["name"]!.GetValue<string>() == SemanticSearchApiFixture.SemanticParameterCode);
        searchParam!["type"]!.GetValue<string>().ShouldBe("special");
    }

    [Fact]
    public async Task GivenObservationDeleted_WhenSemanticSearched_ThenAbsent()
    {
        await _fixture.EnsureSemanticSearchParameterActivatedAsync();
        var tag = Guid.NewGuid().ToString("N");
        const string text = "persistent dry cough for the past two weeks";

        var created = await CreateObservationAsync(tag, text);
        using var delete = await Client.DeleteAsync($"/Observation/{created.Id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await SemanticSearchAsync(tag, text);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        MatchEntries(body).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSemanticChain_WhenSearched_Then400WithSemanticChainDiagnostics()
    {
        await _fixture.EnsureSemanticSearchParameterActivatedAsync();

        // DiagnosticReport.result references Observation, so this chains into the semantic parameter
        // as its terminal -- rejected before any query runs, so no data setup is needed.
        using var response = await GetWithHeadersAsync(
            $"/DiagnosticReport?result:Observation.{SemanticSearchApiFixture.SemanticParameterCode}=anything",
            "strict");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldContain("Semantic search chains are not supported");
    }

    [Fact]
    public async Task GivenSemanticModifier_WhenSearched_Then400()
    {
        await _fixture.EnsureSemanticSearchParameterActivatedAsync();

        using var response = await GetWithHeadersAsync(
            $"/Observation?{SemanticSearchApiFixture.SemanticParameterCode}:text=anything",
            "strict");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
    }

    [Fact]
    public async Task GivenTwoPages_WhenPaged_ThenNoDuplicates()
    {
        await _fixture.EnsureSemanticSearchParameterActivatedAsync();
        var tag = Guid.NewGuid().ToString("N");
        const string baseText = "follow-up visit for chronic knee pain management, variant";

        for (var i = 0; i < 5; i++)
        {
            await CreateObservationAsync(tag, $"{baseText} {i}");
        }

        using var firstResponse = await SemanticSearchAsync(tag, baseText, extraQuery: "&_count=2");
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK, firstBody);
        var firstIds = MatchEntries(firstBody).Select(ReadId).ToArray();

        var nextUrl = JsonNode.Parse(firstBody)!["link"]!.AsArray()
            .SingleOrDefault(link => link!["relation"]!.GetValue<string>() == "next")?["url"]!.GetValue<string>();
        nextUrl.ShouldNotBeNull("Five results paged two at a time must produce a next link for page 1.");

        using var secondResponse = await GetWithHeadersAsync(nextUrl!, "strict");
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK, secondBody);
        var secondIds = MatchEntries(secondBody).Select(ReadId).ToArray();

        firstIds.Intersect(secondIds).ShouldBeEmpty("Page 1 and page 2 must not repeat the same Observation.");
        firstIds.Concat(secondIds).Distinct().Count().ShouldBe(firstIds.Length + secondIds.Length);
    }

    private async Task<ResourceJsonNode> CreateObservationAsync(
        string tag, string text, string status = "final")
    {
        var observation = ObservationBuilder.Create(SchemaProvider)
            .WithCode("55284-4", "http://loinc.org", "Blood pressure systolic and diastolic")
            .WithStatus(status)
            .WithStringValue(text)
            .WithTag(tag)
            .Build();

        return await Harness.CreateResourceAsync(observation);
    }

    private Task<HttpResponseMessage> SemanticSearchAsync(string tag, string text, string extraQuery = "") =>
        GetWithHeadersAsync(
            $"/Observation?_tag={tag}&{SemanticSearchApiFixture.SemanticParameterCode}={Uri.EscapeDataString(text)}{extraQuery}",
            "strict");

    private async Task<HttpResponseMessage> GetWithHeadersAsync(string url, string handling)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Prefer", $"handling={handling}");
        return await Client.SendAsync(request);
    }

    private static JsonNode[] MatchEntries(string bundleJson) =>
        (JsonNode.Parse(bundleJson)!["entry"]?.AsArray() ?? [])
            .Where(entry => entry!["search"]?["mode"]?.GetValue<string>() == "match")
            .Select(entry => entry!)
            .ToArray();

    private static string ReadId(JsonNode entry) => entry["resource"]!["id"]!.GetValue<string>();

    private static double ReadScore(JsonNode entry) => entry["search"]!["score"]!.GetValue<double>();
}
