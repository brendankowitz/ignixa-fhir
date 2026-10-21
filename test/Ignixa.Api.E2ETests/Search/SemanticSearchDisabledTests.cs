// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Ignixa.Api.E2ETests._Infrastructure;
using Shouldly;

namespace Ignixa.Api.E2ETests.Search;

/// <summary>
/// E2E coverage for Review Focus 5 in the slice-1 plan: a semantic <c>special</c> SearchParameter
/// activated while <c>VectorSearch:Enabled</c> is false (the server default) behaves as an unknown
/// parameter -- ignored with an issue under lenient handling, rejected under strict -- and no embedding
/// generator is ever invoked.
/// </summary>
/// <remarks>
/// Uses its own, plain <see cref="IgnixaApiFixture"/> -- not the shared <c>E2ETestCollection</c> fixture
/// every other search test depends on, and not <see cref="SemanticSearchApiFixture"/>, which enables the
/// feature these tests need to stay off -- so activating a custom SearchParameter here cannot change any
/// other E2E test's behavior. Each test gets its own host and owned database (mirrors
/// <c>SqlActivationPreflightTests</c>' per-test host pattern), accepting a slower per-test startup for
/// that isolation.
/// </remarks>
#pragma warning disable CA1001 // xUnit owns disposal through IAsyncLifetime.
public class SemanticSearchDisabledTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private IgnixaApiFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new IgnixaApiFixture();
        await _fixture.InitializeAsync();
        await SemanticSearchApiFixture.ActivateSemanticSearchParameterOnAsync(_fixture.Services);
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task GivenFeatureDisabledFixture_WhenStrictSemanticSearch_Then400()
    {
        using var response = await SearchAsync("strict");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
    }

    [Fact]
    public async Task GivenFeatureDisabledFixture_WhenLenientSemanticSearch_Then200WithParameterIgnored()
    {
        using var response = await SearchAsync("lenient");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"not-supported\"", Case.Insensitive);
    }

    private async Task<HttpResponseMessage> SearchAsync(string handling)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/Observation?{SemanticSearchApiFixture.SemanticParameterCode}=anything");
        request.Headers.Add("Prefer", $"handling={handling}");
        return await _fixture.Client.SendAsync(request);
    }
}
