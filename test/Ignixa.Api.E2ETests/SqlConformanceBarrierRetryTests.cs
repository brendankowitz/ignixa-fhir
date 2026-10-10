using System.Net.Http.Json;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.SqlServer;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Ignixa.Api.E2ETests;

public class SqlConformanceBarrierRetryTests(IgnixaApiFixture fixture) : IClassFixture<IgnixaApiFixture>
{
    [Fact]
    public async Task GivenAWriteExtractedBeforeTheBarrier_WhenTheEventIsAvailable_ThenItRefreshesAndRetriesOnce()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var code = $"barrier-marker-{suffix}";
        var canonical = $"http://example.org/SearchParameter/{code}";
        var identifier = $"marker-{suffix}";
        var versions = fixture.Services.GetRequiredService<IFhirVersionContext>();
        var before = versions.GetDefinitionsHandle(FhirVersion.R4, tenantId: 1);
        var store = fixture.Services.GetRequiredService<ISourceEventStore>();
        var persisted = await store.AppendAsync(
        [
            new NewSourceEvent(
                $"barrier-retry:{Guid.NewGuid():N}",
                nameof(SearchParameterActivated),
                new SearchParameterActivated(
                    canonical,
                    code,
                    "Patient",
                    "Patient.identifier",
                    SearchParamType.Token,
                    "barrier.retry@1.0.0",
                    null,
                    SearchParamId: BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) & int.MaxValue,
                    TargetResourceTypes: null,
                    Components: null,
                    Name: "BarrierMarker",
                    Description: null)),
        ],
        CancellationToken.None);
        var barrier = persisted.Single().EventId;
        barrier.ShouldBeGreaterThan(before.DefinitionsEventId);
        await RaiseBarrierAsync(barrier);

        var id = $"barrier-retry-{suffix}";
        using var response = await fixture.Client.PutAsJsonAsync(
            $"/tenant/1/Patient/{id}",
            new
            {
                resourceType = "Patient",
                id,
                identifier = new[] { new { system = "http://example.org/barrier", value = identifier } }
            });

        response.EnsureSuccessStatusCode();
        var after = versions.GetDefinitionsHandle(FhirVersion.R4, tenantId: 1);
        after.DefinitionsEventId.ShouldBeGreaterThanOrEqualTo(barrier);
        (await CountFailedBarrierTransactionsAsync()).ShouldBeGreaterThanOrEqualTo(1);

        using var search = new HttpRequestMessage(
            HttpMethod.Get,
            $"/tenant/1/Patient?{Uri.EscapeDataString(code)}={Uri.EscapeDataString(identifier)}");
        search.Headers.Add("x-ms-use-partial-indices", "true");
        search.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        using var searchResponse = await fixture.Client.SendAsync(search);
        searchResponse.EnsureSuccessStatusCode();
        using var bundle = JsonDocument.Parse(await searchResponse.Content.ReadAsByteArrayAsync());
        bundle.RootElement.GetProperty("entry")
            .EnumerateArray()
            .Any(entry =>
                entry.TryGetProperty("resource", out var resource) &&
                resource.TryGetProperty("id", out var resourceId) &&
                resourceId.GetString() == id)
            .ShouldBeTrue();
    }

    private async Task RaiseBarrierAsync(long eventId)
    {
        using var command = new SqlCommand(
            """
            UPDATE dbo.Parameters
            SET Bigint = @EventId
            WHERE Id = 'Conformance.MinAcceptedDefinitionsEventId';
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT dbo.Parameters (Id, Bigint)
                VALUES ('Conformance.MinAcceptedDefinitionsEventId', @EventId);
            END
            """);
        command.Parameters.Add("@EventId", System.Data.SqlDbType.BigInt).Value = eventId;
        await fixture.Services.GetRequiredService<ISqlExecutionService>()
            .ExecuteNonQueryAsync(1, command, CancellationToken.None);
    }

    private async Task<int> CountFailedBarrierTransactionsAsync()
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM dbo.Transactions
            WHERE IsCompleted = 1
              AND IsVisible = 1
              AND FailureReason LIKE '%conformance definitions%';
            """);
        var rows = await fixture.Services.GetRequiredService<ISqlExecutionService>()
            .ExecuteReaderAsync(1, command, reader => reader.GetInt32(0), CancellationToken.None);
        return rows.Single();
    }
}
