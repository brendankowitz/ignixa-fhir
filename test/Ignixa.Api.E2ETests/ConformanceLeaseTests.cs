using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using Xunit;

namespace Ignixa.Api.E2ETests;

/// <summary>Spec F15, N7, §4.5: without the staleness lease, search-evaluating requests fail closed with 503.</summary>
public class ConformanceLeaseTests
{
    [Fact]
    public async Task GivenExpiredLease_WhenSearchEvaluatingOperationsRun_ThenTheyFailClosed()
    {
        await using var fixture = new StaleLeaseFixture();
        await fixture.InitializeAsync();
        var client = fixture.Client;
        using var created = await client.PostAsJsonAsync("/tenant/1/Patient", new { resourceType = "Patient", active = true });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();
        using var read = await client.GetAsync($"/tenant/1/Patient/{id}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        using var conditionalCreate = new HttpRequestMessage(HttpMethod.Post, "/tenant/1/Patient")
        {
            Content = JsonContent.Create(new { resourceType = "Patient", active = true }),
        };
        conditionalCreate.Headers.TryAddWithoutValidation("If-None-Exist", "active=true");
        var memberMatch = new
        {
            resourceType = "Parameters",
            parameter = new object[]
            {
                new { name = "MemberPatient", resource = (object)new { resourceType = "Patient", identifier = new[] { new { value = "member-123" } } } },
                new { name = "CoverageToMatch", resource = (object)new { resourceType = "Coverage", subscriberId = "member-123" } },
            },
        };

        using var conditional = await client.SendAsync(conditionalCreate);
        conditional.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await conditional.Content.ReadAsStringAsync());
        HttpResponseMessage[] searches =
        [
            await client.GetAsync("/tenant/1/Patient?active=true&_include=Patient:organization"),
            await client.GetAsync($"/tenant/1/Patient/{id}/Observation"),
            await client.GetAsync($"/tenant/1/Patient/{id}/$everything"),
            await client.PostAsync("/tenant/1/Patient/$member-match", JsonContent.Create(memberMatch, new MediaTypeHeaderValue("application/fhir+json"))),
            await client.GetAsync($"/tenant/1/Patient/{id}/$summary"),
        ];

        foreach (var response in searches)
        {
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, $"{response.RequestMessage!.RequestUri}: {body}");
                response.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromHours(1));
                JsonNode.Parse(body)!["issue"]![0]!["diagnostics"]!.GetValue<string>()
                    .ShouldBe("Conformance state is stale; search is temporarily unavailable.");
            }
        }
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
