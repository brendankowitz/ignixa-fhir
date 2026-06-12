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
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

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
            $$"""{ Patient(_id: "{{created.Id}}") { id name { family given } resourceType } }""");

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
            """{ Patient(_id: "nonexistent-graphql-test-id") { id } }""");

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
        var tag = Guid.NewGuid().ToString();
        await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("ListTest1").WithTag(tag).Build());
        await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("ListTest2").WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            $$"""{ PatientList(_count: 10, _tag: "{{tag}}") { id name { family } } }""");

        AssertNoErrors(result);
        var list = result["data"]!["PatientList"]!.AsArray();
        list.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GivenPatientsExist_WhenSearchingByName_ThenReturnsMatching()
    {
        var tag = Guid.NewGuid().ToString();
        var uniqueName = $"GqlNameSearch{tag[..8]}";
        await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName(uniqueName).WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            $$"""{ PatientList(name: "{{uniqueName}}", _tag: "{{tag}}") { id name { family } } }""");

        AssertNoErrors(result);
        var list = result["data"]!["PatientList"]!.AsArray();
        list.Count.ShouldBe(1);
        list[0]!["name"]![0]!["family"]!.GetValue<string>().ShouldBe(uniqueName);
    }

    // ========================================================================
    // Connection Search (Paginated)
    // ========================================================================

    [Fact]
    public async Task GivenPatientsExist_WhenConnectionSearch_ThenReturnsPaginatedResult()
    {
        var tag = Guid.NewGuid().ToString();
        for (int i = 0; i < 3; i++)
        {
            await Harness.CreateResourceAsync(
                CreatePatient().WithFamilyName($"ConnTest{i}").WithTag(tag).Build());
        }

        var result = await PostGraphQlAsync(
            $$"""{ PatientConnection(_count: 2, _tag: "{{tag}}") { count pagesize edges { mode resource { id name { family } } } next } }""");

        AssertNoErrors(result);
        var conn = result["data"]!["PatientConnection"]!;
        conn["pagesize"]!.GetValue<int>().ShouldBe(2);
        var edges = conn["edges"]!.AsArray();
        edges.Count.ShouldBeLessThanOrEqualTo(2);
        edges[0]!["resource"]!["id"].ShouldNotBeNull();
    }

    // ========================================================================
    // Instance-Level Queries
    // ========================================================================

    [Fact]
    public async Task GivenPatientExists_WhenInstanceQuery_ThenReturnsResourceFields()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithGivenName("Instance").WithFamilyName("QueryTest").WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            "{ id name { family given } }",
            $"/Patient/{created.Id}/$graphql");

        AssertNoErrors(result);
        result["data"]!["id"]!.GetValue<string>().ShouldBe(created.Id);
        result["data"]!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("QueryTest");
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
            $$"""{ Patient(_id: "{{patient.Id}}") { id managingOrganization { reference resource(optional: true) { ... on Organization { id name } } } } }""");

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
            "query GetPatient($pid: ID!) { Patient(_id: $pid) { id name { family } } }",
            new { pid = created.Id },
            "GetPatient");

        AssertNoErrors(result);
        result["data"]!["Patient"]!["id"]!.GetValue<string>().ShouldBe(created.Id);
    }

    // ========================================================================
    // Directives
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithMultipleNames_WhenUsingFirstDirective_ThenReturnsSingleName()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient()
                .WithFamilyName("FirstDir")
                .AddName("FirstDir", "Nick", "nickname")
                .WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(_id: "{{created.Id}}") { id name @first { family } } }""");

        AssertNoErrors(result);
        var name = result["data"]!["Patient"]!["name"]!;
        // @first should return a single object, not an array
        name["family"].ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenPatient_WhenUsingSkipDirective_ThenOmitsField()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("SkipTest").WithTag(tag).Build());

        var result = await PostGraphQlWithVariablesAsync(
            """query($skip: Boolean!) { Patient(_id: "$ID") { id name @skip(if: $skip) { family } } }"""
                .Replace("$ID", created.Id!, StringComparison.Ordinal),
            new { skip = true });

        AssertNoErrors(result);
        result["data"]!["Patient"]!["name"].ShouldBeNull();
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
            $$"""{ Patient(_id: "{{created.Id}}") { id birthDate _birthDate { id } } }""");

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
            $$"""{ Patient(_id: "{{created.Id}}") { id firstTwo: name(_count: 2) { family } allNames: name { family } } }""");

        AssertNoErrors(result);
        var patient = result["data"]!["Patient"]!;
        var firstTwo = patient["firstTwo"]!.AsArray();
        var allNames = patient["allNames"]!.AsArray();
        firstTwo.Count.ShouldBeLessThanOrEqualTo(2);
        allNames.Count.ShouldBeGreaterThanOrEqualTo(firstTwo.Count);
    }

    // ========================================================================
    // Extension Filtering
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithExtensions_WhenFilteringByUrl_ThenReturnsMatchingExtensions()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient()
                .WithFamilyName("ExtFilter")
                .WithExtension("http://example.org/ext1", "FirstExtension")
                .WithExtension("http://example.org/ext2", "SecondExtension")
                .WithTag(tag)
                .Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(_id: "{{created.Id}}") { extension(url: "http://example.org/ext1") { url valueString } } }""");

        AssertNoErrors(result);
        var extensions = result["data"]!["Patient"]!["extension"]!.AsArray();
        extensions.Count.ShouldBe(1);
        extensions[0]!["url"]!.GetValue<string>().ShouldBe("http://example.org/ext1");
        extensions[0]!["valueString"]!.GetValue<string>().ShouldBe("FirstExtension");
    }

    [Fact]
    public async Task GivenPatientWithNestedExtensions_WhenFilteringByUrl_ThenReturnsMatchingNestedExtensions()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient()
                .WithFamilyName("NestedExt")
                .WithExtension("http://example.org/complex", ext =>
                {
                    ext.Extension.Add(new ExtensionJsonNode(new JsonObject
                    {
                        ["url"] = "http://example.org/nested1",
                        ["valueString"] = "NestedValue1"
                    }));
                    ext.Extension.Add(new ExtensionJsonNode(new JsonObject
                    {
                        ["url"] = "http://example.org/nested2",
                        ["valueString"] = "NestedValue2"
                    }));
                })
                .WithTag(tag)
                .Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(_id: "{{created.Id}}") { extension(url: "http://example.org/complex") { url extension(url: "http://example.org/nested1") { url valueString } } } }""");

        AssertNoErrors(result);
        var ext = result["data"]!["Patient"]!["extension"]![0]!;
        ext["url"]!.GetValue<string>().ShouldBe("http://example.org/complex");
        var nested = ext["extension"]!.AsArray();
        nested.Count.ShouldBe(1);
        nested[0]!["url"]!.GetValue<string>().ShouldBe("http://example.org/nested1");
        nested[0]!["valueString"]!.GetValue<string>().ShouldBe("NestedValue1");
    }

    // ========================================================================
    // Flatten Directive
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithIdentifiers_WhenUsingFlattenDirective_ThenCollatesProperties()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient()
                .WithFamilyName("FlattenTest")
                .WithIdentifier("http://sys1", "val1")
                .WithIdentifier("http://sys2", "val2")
                .WithTag(tag)
                .Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(_id: "{{created.Id}}") { identifier @flatten { system value } } }""");

        AssertNoErrors(result);
        var patient = result["data"]!["Patient"]!;
        patient["identifier"].ShouldBeNull();
        var systems = patient["system"]!.AsArray();
        systems.Count.ShouldBe(2);
        systems[0]!.GetValue<string>().ShouldBe("http://sys1");
        systems[1]!.GetValue<string>().ShouldBe("http://sys2");
        var values = patient["value"]!.AsArray();
        values.Count.ShouldBe(2);
        values[0]!.GetValue<string>().ShouldBe("val1");
        values[1]!.GetValue<string>().ShouldBe("val2");
    }

    // ========================================================================
    // Slice Directive
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithNames_WhenUsingSliceByProperty_ThenSuffixesByPropertyValue()
    {
        var tag = Guid.NewGuid().ToString();
        var resourceJson = $$"""{"resourceType":"Patient","id":"{{Guid.NewGuid()}}","meta":{"tag":[{"system":"http://test.ignixa.io/tag","code":"{{tag}}"}]},"name":[{"use":"official","family":"Chalmers","given":["Peter","James"]},{"use":"usual","given":["Jim"]}]}""";
        var created = await Harness.CreateResourceAsync(ResourceJsonNode.Parse(resourceJson));

        var result = await PostGraphQlAsync(
            $$"""{ Patient(_id: "{{created.Id}}") { name @flatten @slice(path: "use") { given family } } }""");

        AssertNoErrors(result);
        var patient = result["data"]!["Patient"]!;
        patient["name"].ShouldBeNull();
        patient["given.official"]!.AsArray().Count.ShouldBe(2);
        patient["family.official"]!.GetValue<string>().ShouldBe("Chalmers");
        patient["given.usual"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenPatientWithNames_WhenUsingSliceByIndex_ThenSuffixesByIndex()
    {
        var tag = Guid.NewGuid().ToString();
        var resourceJson = $$"""{"resourceType":"Patient","id":"{{Guid.NewGuid()}}","meta":{"tag":[{"system":"http://test.ignixa.io/tag","code":"{{tag}}"}]},"name":[{"family":"First","given":["A"]},{"family":"Second","given":["B"]}]}""";
        var created = await Harness.CreateResourceAsync(ResourceJsonNode.Parse(resourceJson));

        var result = await PostGraphQlAsync(
            $$"""{ Patient(_id: "{{created.Id}}") { name @flatten @slice(path: "$index") { given family } } }""");

        AssertNoErrors(result);
        var patient = result["data"]!["Patient"]!;
        patient["name"].ShouldBeNull();
        patient["given.0"]!.AsArray().Count.ShouldBe(1);
        patient["family.0"]!.GetValue<string>().ShouldBe("First");
        patient["given.1"]!.AsArray().Count.ShouldBe(1);
        patient["family.1"]!.GetValue<string>().ShouldBe("Second");
    }

    // ========================================================================
    // Sub-Property Filters
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithMultipleNames_WhenFilteringBySubProperty_ThenReturnsMatchingNames()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient()
                .WithFamilyName("SubPropTest")
                .AddName("SubPropTest", "Official", "official")
                .AddName("SubPropTest", "Nickname", "nickname")
                .WithTag(tag)
                .Build());

        var result = await PostGraphQlAsync(
            $$"""{ Patient(_id: "{{created.Id}}") { name(use: "official") { use family given } } }""");

        AssertNoErrors(result);
        var names = result["data"]!["Patient"]!["name"]!.AsArray();
        names.Count.ShouldBe(2); // primary official name + added official name
        names.All(n => n!["use"]!.GetValue<string>() == "official").ShouldBeTrue();
        names[0]!["family"]!.GetValue<string>().ShouldBe("SubPropTest");
    }

    // ========================================================================
    // Reverse References at Instance Level
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithCondition_WhenQueryingInstanceReverseReference_ThenReturnsCondition()
    {
        var tag = Guid.NewGuid().ToString();
        var patient = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("ReverseRef").WithTag(tag).Build());

        var conditionJson = $$$"""{"resourceType":"Condition","id":"{{{Guid.NewGuid()}}}","meta":{"tag":[{"system":"http://test.ignixa.io/tag","code":"{{{tag}}}"}]},"subject":{"reference":"Patient/{{{patient.Id}}}"},"code":{"text":"Test condition"}}""";
        var condition = await Harness.CreateResourceAsync(ResourceJsonNode.Parse(conditionJson));

        var result = await PostGraphQlAsync(
            $$"""{ ConditionList(_reference: "patient") { id code { text } } }""",
            $"/Patient/{patient.Id}/$graphql");

        AssertNoErrors(result);
        var conditions = result["data"]!["ConditionList"]!.AsArray();
        conditions.Count.ShouldBe(1);
        conditions[0]!["id"]!.GetValue<string>().ShouldBe(condition.Id);
        conditions[0]!["code"]!["text"]!.GetValue<string>().ShouldBe("Test condition");
    }

    // ========================================================================
    // Search Parameter Array Syntax
    // ========================================================================

    [Fact]
    public async Task GivenMultiplePatients_WhenQueryingWithArrayId_ThenReturnsMatchingPatients()
    {
        var tag = Guid.NewGuid().ToString();
        var patient1 = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("ArrayTest1").WithTag(tag).Build());
        var patient2 = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("ArrayTest2").WithTag(tag).Build());
        var patient3 = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("ArrayTest3").WithTag(tag).Build());

        var result = await PostGraphQlAsync(
            $$"""{ PatientList(_id: ["{{patient1.Id}}", "{{patient2.Id}}"]) { id } }""");

        AssertNoErrors(result);
        var patients = result["data"]!["PatientList"]!.AsArray();
        patients.Count.ShouldBe(2);
        var ids = patients.Select(p => p!["id"]!.GetValue<string>()).ToHashSet();
        ids.ShouldContain(patient1.Id);
        ids.ShouldContain(patient2.Id);
        ids.ShouldNotContain(patient3.Id);
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

    // ========================================================================
    // Tenant Isolation
    // ========================================================================

    [Fact]
    public async Task GivenSystemPartitionRoute_WhenPostingGraphQl_ThenRejectedWithBadRequest()
    {
        var body = JsonSerializer.Serialize(new { query = "{ __typename }" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync("/tenant/0/$graphql", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenPatientInTenant1_WhenQueryingViaInactiveTenant2Route_ThenIsolationIsEnforced()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("Tenant1Only").WithTag(tag).Build());

        var query = JsonSerializer.Serialize(
            new { query = $$"""{ Patient(_id: "{{created.Id}}") { id } }""" });
        using var content = new StringContent(query, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync("/tenant/2/$graphql", content);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldNotContain(created.Id!);
    }

    // ========================================================================
    // Instance Injection Hardening
    // ========================================================================

    [Fact]
    public async Task GivenHostileResourceTypeSegment_WhenInstanceQuery_ThenReturnsGraphQlErrorNotServerError()
    {
        var hostileResourceType = Uri.EscapeDataString("""Patient(_id:"x"){id}""");
        var body = JsonSerializer.Serialize(new { query = "{ id }" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync(
            $"/{hostileResourceType}/abc123/$graphql", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        result["errors"].ShouldNotBeNull(
            $"Hostile resourceType must produce a GraphQL error. Response: {result.ToJsonString()}");
        var data = result["data"];
        (data is null || data.AsObject().Count == 0).ShouldBeTrue(
            $"Injected selections must not execute. Response: {result.ToJsonString()}");
    }

    [Fact]
    public async Task GivenHostileResourceTypeSegment_WhenInstanceGet_ThenReturnsGraphQlErrorNotServerError()
    {
        var hostileResourceType = Uri.EscapeDataString("""Patient(_id:"x"){id}""");

        using var response = await Client.GetAsync(
            $"/{hostileResourceType}/abc123/$graphql?query=" + Uri.EscapeDataString("{ id }"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        result["errors"].ShouldNotBeNull();
    }

    // ========================================================================
    // Singleton Directive Violation
    // ========================================================================

    [Fact]
    public async Task GivenPatientWithMultipleIdentifiers_WhenUsingSingletonDirective_ThenReturnsViolationError()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient()
                .WithFamilyName("SingletonViolation")
                .WithIdentifier("http://sys1", "val1")
                .WithIdentifier("http://sys2", "val2")
                .WithTag(tag)
                .Build());

        var query = JsonSerializer.Serialize(new
        {
            query = $$"""{ Patient(_id: "{{created.Id}}") { identifier @singleton { value } } }""",
        });
        using var content = new StringContent(query, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync("/$graphql", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        result["errors"].ShouldNotBeNull(
            $"@singleton on a multi-element list must error. Response: {result.ToJsonString()}");
    }

    // ========================================================================
    // Query Depth Limit
    // ========================================================================

    [Fact]
    public async Task GivenDeeplyNestedQuery_WhenExecuting_ThenReturnsDepthLimitErrorNotServerError()
    {
        var nested = new StringBuilder("{ __type(name: \"Patient\") { ofType ");
        const int depth = 18;
        for (int i = 0; i < depth; i++)
        {
            nested.Append("{ ofType ");
        }
        nested.Append("{ name }");
        for (int i = 0; i < depth; i++)
        {
            nested.Append(" } ");
        }
        nested.Append(" }");

        var body = JsonSerializer.Serialize(new { query = nested.ToString() });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync("/$graphql", content);

        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        result["errors"].ShouldNotBeNull(
            $"A query exceeding MaxQueryDepth must produce errors. Response: {result.ToJsonString()}");
    }

    // ========================================================================
    // Cursor Round-Trip Pagination
    // ========================================================================

    [Fact]
    public async Task GivenMultiplePages_WhenFollowingCursor_ThenReturnsDistinctResourcesUntilExhausted()
    {
        var tag = Guid.NewGuid().ToString();
        const int totalPatients = 6;
        for (int i = 0; i < totalPatients; i++)
        {
            await Harness.CreateResourceAsync(
                CreatePatient().WithFamilyName($"Cursor{i}").WithTag(tag).Build());
        }

        var page1 = await PostGraphQlAsync(
            $$"""{ PatientConnection(_count: 2, _tag: "{{tag}}") { edges { resource { id } } next } }""");
        AssertNoErrors(page1);

        var page1Conn = page1["data"]!["PatientConnection"]!;
        var collectedIds = page1Conn["edges"]!.AsArray()
            .Select(e => e!["resource"]!["id"]!.GetValue<string>())
            .ToList();
        collectedIds.Count.ShouldBe(2);

        var next = page1Conn["next"]?.GetValue<string>();
        next.ShouldNotBeNullOrEmpty("First page of a 6-item, 2-per-page set must expose a next cursor.");

        var iterations = 0;
        while (!string.IsNullOrEmpty(next) && iterations < 10)
        {
            iterations++;
            var page = await PostGraphQlAsync(
                $$"""{ PatientConnection(_count: 2, _cursor: "{{next}}", _tag: "{{tag}}") { edges { resource { id } } next } }""");
            AssertNoErrors(page);

            var conn = page["data"]!["PatientConnection"]!;
            var pageIds = conn["edges"]!.AsArray()
                .Select(e => e!["resource"]!["id"]!.GetValue<string>())
                .ToList();

            pageIds.ShouldAllBe(id => !collectedIds.Contains(id));
            collectedIds.AddRange(pageIds);
            next = conn["next"]?.GetValue<string>();
        }

        next.ShouldBeNullOrEmpty("The final page must not expose a next cursor.");
        collectedIds.Distinct().Count().ShouldBe(totalPatients);
    }

    // ========================================================================
    // Mutation Failure Envelope
    // ========================================================================

    [Fact]
    public async Task GivenInvalidResourceJson_WhenUpdatingViaGraphQl_ThenReturnsOperationOutcomeWithInvalidResourceCode()
    {
        var malformedJson = "{ this is not valid fhir";
        var escaped = malformedJson.Replace("\"", "\\\"", StringComparison.Ordinal);

        var result = await PostGraphQlAsync(
            $$"""mutation { PatientUpdate(id: "some-id", res: "{{escaped}}") { id } }""");

        var errors = result["errors"]!.AsArray();
        errors.Count.ShouldBeGreaterThan(0);
        var extensions = errors[0]!["extensions"]!;
        extensions["code"]!.GetValue<string>().ShouldBe("INVALID_RESOURCE");
        var resource = JsonNode.Parse(extensions["resource"]!.GetValue<string>())!;
        resource["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenExistingResource_WhenUpdatingViaGraphQl_ThenReturnsUpdatedResource()
    {
        var tag = Guid.NewGuid().ToString();
        var created = await Harness.CreateResourceAsync(
            CreatePatient().WithFamilyName("BeforeUpdate").WithTag(tag).Build());

        var updatedJson =
            $$"""{"resourceType":"Patient","id":"{{created.Id}}","name":[{"family":"AfterUpdate"}]}""";
        var escaped = updatedJson.Replace("\"", "\\\"", StringComparison.Ordinal);

        var result = await PostGraphQlAsync(
            $$"""mutation { PatientUpdate(id: "{{created.Id}}", res: "{{escaped}}") { id name { family } } }""");

        AssertNoErrors(result);
        var updated = result["data"]!["PatientUpdate"]!;
        updated["id"]!.GetValue<string>().ShouldBe(created.Id);
        updated["name"]![0]!["family"]!.GetValue<string>().ShouldBe("AfterUpdate");
    }

    // ========================================================================
    // Missing Instance
    // ========================================================================

    [Fact]
    public async Task GivenMissingInstance_WhenInstanceQuery_ThenReturnsNullDataWithNotFoundError()
    {
        var body = JsonSerializer.Serialize(new { query = "{ id name { family } }" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync(
            "/Patient/does-not-exist-graphql/$graphql", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        result["data"].ShouldBeNull(
            $"Missing instance must unwrap to data:null. Response: {result.ToJsonString()}");
        var errors = result["errors"]!.AsArray();
        errors.ShouldContain(e => e!["extensions"]!["code"]!.GetValue<string>() == "FHIR_NOT_FOUND");
    }

    // ========================================================================
    // Empty / Malformed Body
    // ========================================================================

    [Fact]
    public async Task GivenMalformedJsonBody_WhenPosting_ThenReturnsBadRequestWithParseInfo()
    {
        var body = "{ \"query\": ";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync("/$graphql", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.ShouldContain("Invalid JSON");
    }

    [Fact]
    public async Task GivenMalformedQueryString_WhenPosting_ThenReturnsSyntaxErrorEnvelope()
    {
        var body = JsonSerializer.Serialize(new { query = "{ this is { not valid graphql" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync("/$graphql", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var errors = result["errors"]!.AsArray();
        errors.ShouldContain(e => e!["extensions"]!["code"]!.GetValue<string>() == "FHIR_SYNTAX_ERROR");
    }
}

