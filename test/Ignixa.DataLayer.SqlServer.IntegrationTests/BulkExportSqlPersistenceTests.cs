using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.DataLayer.FileSystem.DurableTask;
using Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class BulkExportSqlPersistenceTests
{
    [Fact]
    public async Task GivenSqlCompletionDuringRuntimeRead_WhenPolling_ThenTheHandlerReloadsTheWinningResult()
    {
        var database = await TestTenantDatabase.CreateEmptyAsync();
        try
        {
            var tenants = new TenantStore();
            var repository = CreateRepository<ExportJobDefinition>(database, tenants);
            await repository.CreateAsync(new BackgroundJob<ExportJobDefinition>
            {
                JobId = "export-race", JobType = 1, Status = "Queued",
                Definition = new ExportJobDefinition
                {
                    TenantId = 1, ResourceTypes = ["Patient"], TypeFilters = new Dictionary<string, string>(),
                    OutputFormat = "application/fhir+ndjson", OutputPath = "partition/1/export/export-race"
                }
            });
            var runtime = new CompletingRuntime(async () =>
            {
                var winner = (await repository.GetAsync("export-race", 1))!;
                winner.Status = "Completed";
                winner.Result = JsonNode.Parse("""{"totalResources":7,"exportedFiles":{"Patient":"winning.ndjson"}}""");
                winner.Progress = JsonNode.Parse("""{"progressPercentage":100,"resourcesExported":7}""");
                winner.EndDate = DateTimeOffset.UtcNow;
                await repository.UpdateAsync(winner, 1);
            });
            var handler = new GetJobStatusHandler(new TaskHubClient(runtime),
                CreateRepository<ImportJobDefinition>(database, tenants),
                CreateRepository<ExportJobDefinition>(database, tenants));

            var status = await handler.HandleAsync(
                new GetJobStatusQuery { JobId = "export-race", JobType = "Export", TenantId = 1 },
                CancellationToken.None);

            status.Status.ShouldBe("Completed");
            status.ProgressPercentage.ShouldBe(100);
            JsonSerializer.SerializeToNode(status.Result, JsonSerializerOptions.Web)!["totalResources"]!.GetValue<long>().ShouldBe(7);
            var stored = (await repository.GetAsync("export-race", 1))!;
            stored.Status.ShouldBe("Completed");
            stored.Result!["exportedFiles"]!["Patient"]!.GetValue<string>().ShouldBe("winning.ndjson");
            stored.EndDate.ShouldNotBeNull();
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenPersistedExportMetadata_WhenRecreatingStatusServices_ThenProgressAndAllFilesSurvive()
    {
        var database = await TestTenantDatabase.CreateEmptyAsync();
        try
        {
            var tenants = new TenantStore();
            var exports = CreateRepository<ExportJobDefinition>(database, tenants);
            await exports.CreateAsync(new BackgroundJob<ExportJobDefinition>
            {
                JobId = "export-job", JobType = 1, Status = "Running",
                Definition = new ExportJobDefinition
                {
                    TenantId = 1, ResourceTypes = ["Patient"], TypeFilters = new Dictionary<string, string>(),
                    OutputFormat = "application/fhir+ndjson", OutputPath = "partition/1/export/export-job"
                },
                Progress = JsonNode.Parse("""
                    {"progressPercentage":50,"resourcesExported":1,"bytesWritten":100,"currentPhase":"WorkerExecution"}
                    """)
            });

            var running = await CreateHandler(database, tenants).HandleAsync(Query(), CancellationToken.None);
            running.Status.ShouldBe("Running");
            running.ProgressPercentage.ShouldBe(50);

            var reloaded = (await CreateRepository<ExportJobDefinition>(database, tenants)
                .GetAsync("export-job", 1))!;
            reloaded.Progress!["resourcesExported"]!.GetValue<int>().ShouldBe(1);
            reloaded.Status = "Completed";
            reloaded.EndDate = DateTimeOffset.UtcNow;
            reloaded.Progress = JsonNode.Parse("""
                {"progressPercentage":100,"resourcesExported":3,"bytesWritten":300,"currentPhase":"Aggregation"}
                """);
            reloaded.Result = JsonNode.Parse("""
                {"totalResources":3,
                 "exportedFiles":{
                   "Patient-1-9":"partition/1/export/export-job/Patient-1-9.ndjson",
                   "Patient-10-19":"partition/1/export/export-job/Patient-10-19.ndjson"},
                 "exportedFileCounts":{"Patient-1-9":2,"Patient-10-19":1}}
                """);
            await CreateRepository<ExportJobDefinition>(database, tenants).UpdateAsync(reloaded, 1);

            var completed = await CreateHandler(database, tenants).HandleAsync(Query(), CancellationToken.None);
            completed.Status.ShouldBe("Completed");
            completed.ProgressPercentage.ShouldBe(100);
            var result = JsonSerializer.SerializeToNode(completed.Result, JsonSerializerOptions.Web)!;
            result["totalResources"]!.GetValue<long>().ShouldBe(3);

            var persisted = (await CreateRepository<ExportJobDefinition>(database, tenants)
                .GetAsync("export-job", 1))!;
            var files = persisted.Result!["exportedFiles"]!.AsObject();
            files.Count.ShouldBe(2);
            files["Patient-1-9"]!.GetValue<string>().ShouldBe("partition/1/export/export-job/Patient-1-9.ndjson");
            files["Patient-10-19"]!.GetValue<string>().ShouldBe("partition/1/export/export-job/Patient-10-19.ndjson");
            persisted.Result["exportedFileCounts"]!["Patient-1-9"]!.GetValue<long>().ShouldBe(2);
            persisted.Result["exportedFileCounts"]!["Patient-10-19"]!.GetValue<long>().ShouldBe(1);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static GetJobStatusQuery Query() => new() { JobId = "export-job", JobType = "Export", TenantId = 1 };

    private static GetJobStatusHandler CreateHandler(TestTenantDatabase database, ITenantConfigurationStore tenants) =>
        new(new TaskHubClient(new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance)),
            CreateRepository<ImportJobDefinition>(database, tenants),
            CreateRepository<ExportJobDefinition>(database, tenants));

    private static SqlServerBackgroundJobRepository<T> CreateRepository<T>(TestTenantDatabase database, ITenantConfigurationStore tenants)
        where T : class, IJobDefinition =>
        new(database.SqlExecutionService, 1, tenants, NullLogger<SqlServerBackgroundJobRepository<T>>.Instance);

    private sealed class CompletingRuntime(Func<Task> complete)
        : InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance), IOrchestrationServiceClient
    {
        async Task<IList<OrchestrationState>> IOrchestrationServiceClient.GetOrchestrationStateAsync(string instanceId, bool allExecutions)
        {
            await complete();
            return new List<OrchestrationState> { new() { OrchestrationStatus = OrchestrationStatus.Running } };
        }
    }

    private sealed class TenantStore : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = new() { TenantId = 1, DisplayName = "Bulk export test tenant", FhirVersion = "4.0" };
        public TenantMode Mode => TenantMode.Isolated;
        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default) =>
            new(tenantId == 1 ? _tenant : null);
        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default) => new(new[] { _tenant });
        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default) => new((TenantConfiguration?)null);
    }
}
