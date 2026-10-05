using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Shouldly;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class DeletedResourceResponseTests(IgnixaApiFixture fixture)
{
    private static readonly string[] RoutePrefixes = ["", "tenant/1/"];

    [Theory]
    [InlineData("GET", null, null)]
    [InlineData("HEAD", null, null)]
    [InlineData("GET", "If-None-Match", "W/\"1\"")]
    [InlineData("HEAD", "If-None-Match", "W/\"1\"")]
    [InlineData("GET", "If-None-Match", "W/\"2\"")]
    [InlineData("HEAD", "If-None-Match", "W/\"2\"")]
    [InlineData("GET", "If-Modified-Since", "Fri, 01 Jan 2100 00:00:00 GMT")]
    [InlineData("HEAD", "If-Modified-Since", "Fri, 01 Jan 2100 00:00:00 GMT")]
    public async Task GivenDeletedResource_WhenReadingWithOrWithoutConditions_ThenReturnsGone(
        string method, string? header, string? value)
    {
        string id = await CreateDeletedPatientAsync();
        foreach (string prefix in RoutePrefixes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/{prefix}Patient/{id}");
            if (header is not null)
            {
                request.Headers.Add(header, value);
            }

            using var response = await fixture.Client.SendAsync(request);

            await AssertGoneAsync(response, method, id);
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task GivenDeletedVersion_WhenReadingVersion_ThenReturnsGone(string method)
    {
        string id = await CreateDeletedPatientAsync();
        foreach (string prefix in RoutePrefixes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/{prefix}Patient/{id}/_history/2");

            using var response = await fixture.Client.SendAsync(request);

            await AssertGoneAsync(response, method, id);
        }
    }

    private async Task<string> CreateDeletedPatientAsync()
    {
        string id = Guid.NewGuid().ToString("N");
        using var content = new StringContent(
            $$"""{"resourceType":"Patient","id":"{{id}}","active":true}""",
            Encoding.UTF8, "application/fhir+json");
        using var created = await fixture.Client.PutAsync($"/tenant/1/Patient/{id}", content);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var deleted = await fixture.Client.DeleteAsync($"/tenant/1/Patient/{id}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return id;
    }

    private static async Task AssertGoneAsync(HttpResponseMessage response, string method, string id)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/fhir+json");
        response.Headers.ETag?.ToString().ShouldBe("W/\"2\"");
        response.Content.Headers.LastModified.ShouldNotBeNull();
        string body = await response.Content.ReadAsStringAsync();
        if (method == "HEAD")
        {
            body.ShouldBeEmpty();
            return;
        }

        var outcome = JsonNode.Parse(body)!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        outcome["issue"]![0]!["code"]!.GetValue<string>().ShouldBe("deleted");
        outcome["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain($"Patient/{id}");
    }
}
