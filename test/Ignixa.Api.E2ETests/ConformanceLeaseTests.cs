using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using Xunit;

namespace Ignixa.Api.E2ETests;

public class ConformanceLeaseTests
{
    [Fact]
    public async Task GivenExpiredLease_WhenSearchingAndReadingOrWriting_ThenOnlySearchFailsClosed()
    {
        await using var fixture = new StaleLeaseFixture();
        await fixture.InitializeAsync();
        var client = fixture.Client;
        using var patient = new StringContent(
            """{"resourceType":"Patient","active":true}""",
            Encoding.UTF8,
            "application/fhir+json");

        using var created = await client.PostAsync("/tenant/1/Patient", patient);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();

        using var read = await client.GetAsync($"/tenant/1/Patient/{id}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());

        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Post, "/tenant/1/Patient")
        {
            Content = new StringContent(
                """{"resourceType":"Patient","active":true}""",
                Encoding.UTF8,
                "application/fhir+json")
        };
        conditionalRequest.Headers.TryAddWithoutValidation("If-None-Exist", "active=true");
        using var conditional = await client.SendAsync(conditionalRequest);
        conditional.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await conditional.Content.ReadAsStringAsync());

        using var search = await client.GetAsync("/tenant/1/Patient?active=true&_include=Patient:organization");
        using var compartment = await client.GetAsync($"/tenant/1/Patient/{id}/Observation");
        using var everything = await client.GetAsync($"/tenant/1/Patient/{id}/$everything");

        search.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await search.Content.ReadAsStringAsync());
        compartment.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await compartment.Content.ReadAsStringAsync());
        everything.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await everything.Content.ReadAsStringAsync());
        search.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromHours(1));
        var outcome = JsonNode.Parse(await search.Content.ReadAsStringAsync())!;
        outcome["issue"]![0]!["diagnostics"]!.GetValue<string>()
            .ShouldBe("Conformance state is stale; search is temporarily unavailable.");
    }

    private sealed class StaleLeaseFixture : IgnixaApiFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Conformance:MaxStaleness", "00:00:00.001");
            builder.UseSetting("Conformance:SyncIntervalSeconds", "3600");
            base.ConfigureWebHost(builder);
        }
    }
}
