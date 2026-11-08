using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.SqlServer;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.E2ETests;

/// <summary>Spec F4, §5.3: a writer behind the barrier is rejected once, refreshes its definitions and retries.</summary>
public class SqlConformanceBarrierRetryTests(IgnixaApiFixture fixture) : IClassFixture<IgnixaApiFixture>
{
    [Fact]
    public async Task GivenAWriteExtractedBeforeTheBarrier_WhenTheEventIsAvailable_ThenItRefreshesAndRetriesOnce()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var code = $"barrier-marker-{suffix}";
        var versions = fixture.Services.GetRequiredService<IFhirVersionContext>();
        var before = versions.GetDefinitionsHandle(FhirVersion.R4, tenantId: 1);
        var activated = new SearchParameterActivated(
            $"http://example.org/SearchParameter/{code}", code, "Patient", "Patient.identifier", SearchParamType.Token,
            "barrier.retry@1.0.0", null, SearchParamId: BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) & int.MaxValue,
            TargetResourceTypes: null, Components: null, Name: "BarrierMarker", Description: null);
        var barrier = (await fixture.Services.GetRequiredService<ISourceEventStore>().AppendAsync(
            [new NewSourceEvent($"barrier-retry:{suffix}", nameof(SearchParameterActivated), activated)],
            CancellationToken.None)).Single().EventId;
        barrier.ShouldBeGreaterThan(before.DefinitionsEventId);
        await ExecuteAsync(
            $"""
             UPDATE dbo.Parameters SET Bigint = {barrier} WHERE Id = 'Conformance.MinAcceptedDefinitionsEventId';
             IF @@ROWCOUNT = 0 INSERT dbo.Parameters (Id, Bigint) VALUES ('Conformance.MinAcceptedDefinitionsEventId', {barrier});
             """);

        var id = $"barrier-retry-{suffix}";
        using var response = await fixture.Client.PutAsJsonAsync(
            $"/tenant/1/Patient/{id}",
            new { resourceType = "Patient", id, identifier = new[] { new { system = "http://example.org/barrier", value = suffix } } });

        response.EnsureSuccessStatusCode();
        versions.GetDefinitionsHandle(FhirVersion.R4, tenantId: 1).DefinitionsEventId.ShouldBeGreaterThanOrEqualTo(barrier);
        (await ExecuteAsync(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 1 AND FailureReason LIKE '%conformance definitions%';"))
            .ShouldBeGreaterThanOrEqualTo(1);
        using var search = new HttpRequestMessage(HttpMethod.Get, $"/tenant/1/Patient?{code}={suffix}");
        search.Headers.Add("x-ms-use-partial-indices", "true");
        using var searchResponse = await fixture.Client.SendAsync(search);
        searchResponse.EnsureSuccessStatusCode();
        JsonNode.Parse(await searchResponse.Content.ReadAsStringAsync())!["entry"]!.AsArray()
            .Select(entry => entry!["resource"]?["id"]?.GetValue<string>())
            .ShouldContain(id);
    }

    /// <summary>Runs <paramref name="sql"/> against tenant 1 and returns its first column of the first row, or 0.</summary>
    private async Task<int> ExecuteAsync(string sql)
    {
#pragma warning disable CA2100 // Test-controlled SQL.
        using var command = new SqlCommand(sql);
#pragma warning restore CA2100
        var rows = await fixture.Services.GetRequiredService<ISqlExecutionService>()
            .ExecuteReaderAsync(1, command, reader => reader.GetInt32(0), CancellationToken.None);
        return rows.SingleOrDefault();
    }
}
