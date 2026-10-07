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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.E2ETests;

public class SqlConformanceBarrierRetryTests(IgnixaApiFixture fixture) : IClassFixture<IgnixaApiFixture>
{
    [Fact]
    public async Task GivenAWriteExtractedBeforeTheBarrier_WhenTheEventIsAvailable_ThenItRefreshesAndRetriesOnce()
    {
        var versions = fixture.Services.GetRequiredService<IFhirVersionContext>();
        var before = versions.GetDefinitionsHandle(FhirVersion.R4, tenantId: 1);
        var store = fixture.Services.GetRequiredService<ISourceEventStore>();
        var persisted = await store.AppendAsync(
        [
            new NewSourceEvent(
                $"barrier-retry:{Guid.NewGuid():N}",
                nameof(PackageActivated),
                new PackageActivated("barrier.retry", "1.0.0", [])),
        ],
        CancellationToken.None);
        var barrier = persisted.Single().EventId;
        barrier.ShouldBeGreaterThan(before.DefinitionsEventId);
        await RaiseBarrierAsync(barrier);

        var id = $"barrier-retry-{Guid.NewGuid():N}";
        using var response = await fixture.Client.PutAsJsonAsync(
            $"/tenant/1/Patient/{id}",
            new { resourceType = "Patient", id });

        response.EnsureSuccessStatusCode();
        var after = versions.GetDefinitionsHandle(FhirVersion.R4, tenantId: 1);
        after.DefinitionsEventId.ShouldBeGreaterThanOrEqualTo(barrier);
        (await CountFailedBarrierTransactionsAsync()).ShouldBeGreaterThanOrEqualTo(1);
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
