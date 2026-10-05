// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;

namespace Ignixa.Api.E2ETests.Operations.GraphQl;

/// <summary>
/// Core smoke tests for FHIR $graphql endpoints.
/// Tests execute only when $graphql is advertised in capability statement operations.
/// </summary>
public class GraphQlSmokeTests : CapabilityDrivenTestBase
{
    public GraphQlSmokeTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task GivenGraphQlAdvertised_WhenPostingSystemQuery_ThenReturnsDataWithoutErrors()
    {
        // Arrange
        RequireOperationAnywhere("graphql");
        var query = """{"query":"query { __typename }"}""";
        using var content = new StringContent(query, Encoding.UTF8, "application/json");

        // Act
        using var response = await Client.PostAsync("/$graphql", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        AssertGraphQlSuccessEnvelope(responseJson);
    }

    [Fact]
    public async Task GivenGraphQlAdvertisedAndPatientExists_WhenPostingInstanceQuery_ThenReturnsDataWithoutErrors()
    {
        // Arrange
        RequireOperationAnywhere("graphql");
        var createdPatient = await Harness.CreateResourceAsync(CreatePatient().WithTag(Guid.NewGuid().ToString()).Build());
        var query = """{"query":"{ id }"}""";
        using var content = new StringContent(query, Encoding.UTF8, "application/json");

        // Act
        using var response = await Client.PostAsync($"/Patient/{createdPatient.Id}/$graphql", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        AssertGraphQlSuccessEnvelope(responseJson);
    }

    [Fact]
    public async Task GivenGraphQlAdvertised_WhenGettingSystemQuery_ThenReturnsDataWithoutErrors()
    {
        // Arrange
        RequireOperationAnywhere("graphql");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/$graphql?query=query%20%7B%20__typename%20%7D");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Act
        using var response = await Client.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        AssertGraphQlSuccessEnvelope(responseJson);
    }

    private static void AssertGraphQlSuccessEnvelope(string responseJson)
    {
        var json = JsonNode.Parse(responseJson);
        json.ShouldNotBeNull("GraphQL response should be valid JSON");

        json!["data"].ShouldNotBeNull("GraphQL response should contain 'data'");
        json["errors"].ShouldBeNull("GraphQL smoke response should not contain 'errors'");
    }
}
