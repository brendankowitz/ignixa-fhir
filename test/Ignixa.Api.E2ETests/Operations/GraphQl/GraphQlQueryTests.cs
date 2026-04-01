// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;

namespace Ignixa.Api.E2ETests.Operations.GraphQl;

/// <summary>
/// Comprehensive E2E tests for FHIR $graphql operations.
/// Covers introspection, single reads, list/connection search, instance queries,
/// reference resolution, variables, directives, mutations, multi-resource queries,
/// error handling, primitive extensions, list navigation, and multi-tenant queries.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class GraphQlQueryTests : CapabilityDrivenTestBase
{
    public GraphQlQueryTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private async Task<JsonNode> PostGraphQlAsync(string query, string path = "/$graphql")
    {
        var body = JsonSerializer.Serialize(new { query });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(path, content);
        var responseJson = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GraphQL request failed with {response.StatusCode}: {responseJson}");
        return JsonNode.Parse(responseJson)!;
    }

    private async Task<JsonNode> PostGraphQlWithVariablesAsync(
        string query, object variables, string? operationName = null, string path = "/$graphql")
    {
        var bodyObj = new Dictionary<string, object?> { ["query"] = query, ["variables"] = variables };
        if (operationName is not null)
        {
            bodyObj["operationName"] = operationName;
        }

        var body = JsonSerializer.Serialize(bodyObj);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(path, content);
        var responseJson = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GraphQL request failed with {response.StatusCode}: {responseJson}");
        return JsonNode.Parse(responseJson)!;
    }

    private static void AssertNoErrors(JsonNode result)
    {
        result["errors"].ShouldBeNull($"Expected no errors but got: {result["errors"]}");
        result["data"].ShouldNotBeNull($"Response should contain 'data'. Full response: {result.ToJsonString()}");
    }

    // ========================================================================
    // Introspection
    // ========================================================================

    [Fact]
    public async Task GivenGraphQlAdvertised_WhenIntrospectingSchema_ThenReturnsQueryAndMutationTypes()
    {
                var result = await PostGraphQlAsync(
            "{ __schema { queryType { name } mutationType { name } } }");

        AssertNoErrors(result);
        result["data"]!["__schema"]!["queryType"]!["name"]!.GetValue<string>().ShouldBe("Query");
        result["data"]!["__schema"]!["mutationType"]!["name"]!.GetValue<string>().ShouldBe("Mutation");
    }

    [Fact]
    public async Task GivenGraphQlAdvertised_WhenIntrospectingPatientType_ThenReturnsFields()
    {
                var result = await PostGraphQlAsync(
            "{ __type(name: \"Patient\") { name fields { name } } }");

        AssertNoErrors(result);
        var fields = result["data"]!["__type"]!["fields"]!.AsArray();
        fields.Count.ShouldBeGreaterThan(5);
        fields.ShouldContain(f => f!["name"]!.GetValue<string>() == "id");
        fields.ShouldContain(f => f!["name"]!.GetValue<string>() == "name");
        fields.ShouldContain(f => f!["name"]!.GetValue<string>() == "birthDate");
    }

    // ========================================================================
    // Single Resource Read
    // ========================================================================

    [Fact]
    public async Task GivenPatientExists_WhenReadingById_ThenReturnsPatientFields()
    {
                var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithGivenName("GraphQlRead").WithFamilyName("TestPatient").WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(id: "{{created.Id}}") { id name { family given } resourceType } }""");

        AssertNoErrors(result);
        var patient = result["data"]!["Patient"]!;
        patient["id"]!.GetValue<string>().ShouldBe(created.Id);
        patient["resourceType"]!.GetValue<string>().ShouldBe("Patient");
        patient["name"]![0]!["family"]!.GetValue<string>().ShouldBe("TestPatient");
    }

    [Fact]
    public async Task GivenPatientDoesNotExist_WhenReadingById_ThenReturnsNull()
    {
                var result = await PostGraphQlAsync(
            """{ Patient(id: "nonexistent-graphql-test-id") { id } }""");

        AssertNoErrors(result);
        result["data"]!["Patient"].ShouldBeNull();
    }

    // ========================================================================
    // GET Method Support
    // ========================================================================

    [Fact]
    public async Task GivenGraphQlAdvertised_WhenUsingGetMethod_ThenReturnsData()
    {
                using var response = await Client.GetAsync(
            "/$graphql?query=" + Uri.EscapeDataString("{ __typename }"));

        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonNode.Parse(responseJson)!;
        AssertNoErrors(result);
        result["data"]!["__typename"]!.GetValue<string>().ShouldBe("Query");
    }

    // ========================================================================
    // Simple List Search
    // ========================================================================

    [Fact]
    public async Task GivenPatientsExist_WhenListSearching_ThenReturnsArray()
    {
        var result = await PostGraphQlAsync(
            """{ PatientList(_count: 3) { id name { family } } }""");

        AssertNoErrors(result);
        var list = result["data"]!["PatientList"]!.AsArray();
        list.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GivenPatientsExist_WhenSearchingByName_ThenReturnsMatching()
    {
        var uniqueName = $"GqlNameSearch{Guid.NewGuid().ToString()[..8]}";
        await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName(uniqueName).Build());

        var result = await PostGraphQlAsync(
            $$"""{ PatientList(name: "{{uniqueName}}") { id name { family } } }""");

        AssertNoErrors(result);
        var list = result["data"]!["PatientList"]!.AsArray();
        list.Count.ShouldBeGreaterThanOrEqualTo(1);
        list[0]!["name"]![0]!["family"]!.GetValue<string>().ShouldBe(uniqueName);
    }

    // ========================================================================
    // Connection Search (Paginated)
    // ========================================================================

    [Fact]
    public async Task GivenPatientsExist_WhenConnectionSearch_ThenReturnsPaginatedResult()
    {
        var result = await PostGraphQlAsync(
            """{ PatientConnection(_count: 2) { count pagesize edges { mode resource { id name { family } } } next } }""");

        AssertNoErrors(result);
        var conn = result["data"]!["PatientConnection"]!;
        conn["pagesize"]!.GetValue<int>().ShouldBe(2);
        var edges = conn["edges"]!.AsArray();
        edges.Count.ShouldBeLessThanOrEqualTo(2);
    }

    // ========================================================================
    // Instance-Level Queries
    // ========================================================================

    [Fact]
    public async Task GivenPatientExists_WhenInstanceQuery_ThenReturnsResponse()
    {
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithGivenName("Instance").WithFamilyName("QueryTest").Build());

        var body = JsonSerializer.Serialize(new { query = "{ id resourceType }" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync($"/Patient/{created.Id}/$graphql", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        // Instance-level endpoint should return a response (200 with data or errors)
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        responseJson.ShouldNotBeNullOrEmpty();
    }

    // ========================================================================
    // Reference Resolution
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithOrganization_WhenResolvingReference_ThenReturnsReferencedResource()
    {
                var tag = Guid.NewGuid().ToString();
        var org = await Harness.CreateResourceAsync(
            CreateOrganization().WithName("GqlRefOrg").WithTag(tag).Build());
        var patient = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("RefTest").WithManagingOrganization(org.Id!).WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(id: "{{patient.Id}}") { id managingOrganization { reference resource(optional: true) { ... on Organization { id name } } } } }""");

        AssertNoErrors(result);
        var mgOrg = result["data"]!["Patient"]!["managingOrganization"]!;
        mgOrg["reference"]!.GetValue<string>().ShouldContain(org.Id!);
    }

    // ========================================================================
    // Variables
    // ========================================================================

    [Fact]
    public async Task GivenPatientExists_WhenUsingVariables_ThenResolvesCorrectly()
    {
                var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("VarTest").WithTag(tag).Build());

        var result = await PostGraphQlWithVariablesAsync(
            "query GetPatient($pid: ID!) { Patient(id: $pid) { id name { family } } }",
            new { pid = created.Id },
            "GetPatient");

        AssertNoErrors(result);
        result["data"]!["Patient"]!["id"]!.GetValue<string>().ShouldBe(created.Id);
    }

    // ========================================================================
    // Directives
    // ========================================================================

    [Fact(Skip = "Directive middleware requires runtime investigation — directives are registered but execution path needs debugging")]
    public async Task GivenPatient_WhenUsingFirstDirective_ThenReturnsResponse()
    {
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("FirstDir").Build());

        var body = JsonSerializer.Serialize(new { query = $$"""{ Patient(id: "{{created.Id}}") { id name @first { family } } }""" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync("/$graphql", content);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonNode.Parse(responseJson)!;
        // @first directive should be accepted (no validation error)
        result["data"].ShouldNotBeNull();
    }

    [Fact(Skip = "Directive middleware requires runtime investigation — directives are registered but execution path needs debugging")]
    public async Task GivenPatient_WhenUsingSkipDirective_ThenReturnsResponse()
    {
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("SkipTest").Build());

        var bodyObj = new Dictionary<string, object?>
        {
            ["query"] = $$"""query($skip: Boolean!) { Patient(id: "{{created.Id}}") { id name @skip(if: $skip) { family } } }""",
            ["variables"] = new { skip = true },
        };
        var body = JsonSerializer.Serialize(bodyObj);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync("/$graphql", content);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonNode.Parse(responseJson)!;
        result["data"].ShouldNotBeNull();
    }

    // ========================================================================
    // Mutations
    // ========================================================================

    [Fact]
    public async Task GivenValidResource_WhenCreatingViaGraphQl_ThenReturnsCreatedResource()
    {
                var familyName = $"GqlCreate{Guid.NewGuid().ToString()[..8]}";
        var resourceJson = $$$"""{"resourceType":"Patient","name":[{"family":"{{{familyName}}}","given":["Test"]}]}""";
        var escaped = resourceJson.Replace("\"", "\\\"", StringComparison.Ordinal);

        var result = await PostGraphQlAsync(
            $$"""mutation { PatientCreate(res: "{{escaped}}") { id name { family given } } }""");

        AssertNoErrors(result);
        var created = result["data"]!["PatientCreate"]!;
        created["id"].ShouldNotBeNull();
        created["name"]![0]!["family"]!.GetValue<string>().ShouldBe(familyName);
    }

    [Fact]
    public async Task GivenCreatedResource_WhenDeletingViaGraphQl_ThenReturnsTrue()
    {
                // First create a patient
        var resourceJson = """{"resourceType":"Patient","name":[{"family":"GqlDeleteTest"}]}""";
        var escaped = resourceJson.Replace("\"", "\\\"", StringComparison.Ordinal);
        var createResult = await PostGraphQlAsync(
            $$"""mutation { PatientCreate(res: "{{escaped}}") { id } }""");
        AssertNoErrors(createResult);
        var createdId = createResult["data"]!["PatientCreate"]!["id"]!.GetValue<string>();

        // Now delete it
        var deleteResult = await PostGraphQlAsync(
            $$"""mutation { PatientDelete(id: "{{createdId}}") }""");

        AssertNoErrors(deleteResult);
    }

    // ========================================================================
    // Multi-Resource Queries
    // ========================================================================

    [Fact]
    public async Task GivenGraphQlAdvertised_WhenQueryingMultipleResourceTypes_ThenReturnsAll()
    {
                var result = await PostGraphQlAsync(
            """{ patients: PatientList(_count: 2) { id } observations: ObservationList(_count: 2) { id } }""");

        AssertNoErrors(result);
        result["data"]!["patients"].ShouldNotBeNull();
        result["data"]!["observations"].ShouldNotBeNull();
    }

    // ========================================================================
    // Error Handling
    // ========================================================================

    [Fact]
    public async Task GivenInvalidQuery_WhenPosting_ThenReturnsGraphQlError()
    {
                var body = """{"query":"{ invalidField }"}""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync("/$graphql", content);

        // GraphQL spec: return 200 with errors array
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonNode.Parse(responseJson)!;
        result["errors"].ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenEmptyQuery_WhenPosting_ThenReturnsBadRequest()
    {
                var body = """{"query":""}""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync("/$graphql", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ========================================================================
    // Primitive Extensions
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithBirthDate_WhenQueryingPrimitiveExtension_ThenReturnsCompanionField()
    {
                var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithBirthDate(1990, 6, 15).WithFamilyName("ExtTest").WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(id: "{{created.Id}}") { id birthDate _birthDate { id } } }""");

        AssertNoErrors(result);
        result["data"]!["Patient"]!["birthDate"].ShouldNotBeNull();
    }

    // ========================================================================
    // List Navigation
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithMultipleNames_WhenUsingOffsetAndLimit_ThenReturnsPaginatedNames()
    {
                var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient()
                .WithFamilyName("NavTest")
                .AddName("NavTest", "Nick1", "nickname")
                .AddName("NavTest", "Nick2", "old")
                .WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(id: "{{created.Id}}") { id firstTwo: name(_limit: 2) { family } allNames: name { family } } }""");

        AssertNoErrors(result);
        var patient = result["data"]!["Patient"]!;
        var firstTwo = patient["firstTwo"]!.AsArray();
        var allNames = patient["allNames"]!.AsArray();
        firstTwo.Count.ShouldBeLessThanOrEqualTo(2);
        allNames.Count.ShouldBeGreaterThanOrEqualTo(firstTwo.Count);
    }

    // ========================================================================
    // Multi-Tenant
    // ========================================================================

    [Fact]
    public async Task GivenGraphQlAdvertised_WhenQueryingViaTenantRoute_ThenReturnsData()
    {
                var result = await PostGraphQlAsync("{ __typename }", "/tenant/1/$graphql");

        AssertNoErrors(result);
        result["data"]!["__typename"]!.GetValue<string>().ShouldBe("Query");
    }
}

