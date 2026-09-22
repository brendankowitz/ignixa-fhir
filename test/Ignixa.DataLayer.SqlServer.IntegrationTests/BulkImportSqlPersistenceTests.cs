using System.Text;
using System.Text.Json;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Import.Activities;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage.Infrastructure;
using Ignixa.DataLayer.FileSystem.DurableTask;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using Shouldly;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class BulkImportSqlPersistenceTests
{
    [Fact]
    public async Task GivenNdjsonWithAnInvalidResource_WhenImportingToSql_ThenCountsResourcesAndRestartedJobPollingAgree()
    {
        var database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        var directory = Path.GetFullPath(Path.Combine("bulk-operation-test-blobs", Guid.NewGuid().ToString("N")));
        try
        {
            var tenants = new TenantStore();
            var blobs = new LocalFileBlobClient(Options.Create(new LocalFileBlobStorageOptions { RootDirectory = directory }),
                NullLogger<LocalFileBlobClient>.Instance);
            await blobs.WriteBlobAsync("input.ndjson", new MemoryStream(Encoding.UTF8.GetBytes(new string('\n', 1001) + """
                {"resourceType":"Patient","id":"bulk-import-p1","identifier":[{"system":"urn:bulk-import-test","value":"find-me"}]}
                {"resourceType":"Observation","id":"wrong-type"}
                {"resourceType":"Patient","id":"bulk-import-p2","active":true}

                """)));
            using var versions = new FhirVersionContext(NullLoggerFactory.Instance,
                new SearchParameterResolutionOptions(), NullFhirBaseUriProvider.Instance);
            var definitions = versions.GetSearchParameterDefinitionManager(FhirVersion.R4);
            using var cache = new SqlServerSearchIndexReferenceDataCache(database.SqlExecutionService, 1,
                NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
            await cache.PreloadResourceTypesAsync(CancellationToken.None);
            await cache.SyncSearchParametersToDatabaseAsync(
                definitions.GetSearchParameters("Patient").Select(parameter => parameter.Url.OriginalString),
                definitions, CancellationToken.None);
            var repository = CreateJobRepository<ImportJobDefinition>(database, tenants);
            await repository.CreateAsync(new BackgroundJob<ImportJobDefinition>
            {
                JobId = "bulk-import-job", JobType = 2, Status = "Running",
                Definition = new ImportJobDefinition
                {
                    TenantId = 1, InputFormat = "application/fhir+ndjson", InputSource = "Patient",
                    Mode = "IncrementalLoad", InputFiles = [new InputFileInfo { Type = "Patient", Url = "input.ndjson" }]
                }
            });
            var context = new TaskContext(new OrchestrationInstance { InstanceId = "bulk-import-job" });
            var activity = new StreamingImportFileActivity(new RepositoryFactory(database.Repository), versions, tenants,
                blobs, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Import:ConsumerCount"] = "1"
                }).Build(), new FhirRequestContextAccessor(), NullLogger<StreamingImportFileActivity>.Instance);

            var outputJson = await activity.RunAsync(context, JsonSerializer.Serialize(new[]
            {
                new StreamingImportFileInput
                {
                    JobId = "bulk-import-job", TenantId = 1, ResourceType = "Patient", FileUrl = "input.ndjson",
                    Mode = "IncrementalLoad", BatchSize = 2, ChannelCapacity = 2
                }
            }));
            var output = JsonSerializer.Deserialize<StreamingImportFileOutput>(outputJson)!;
            output.SuccessCount.ShouldBe(2);
            output.ErrorCount.ShouldBe(1);
            (await database.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM dbo.Resource r
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.Transactions t
                    WHERE r.ResourceSurrogateId BETWEEN t.SurrogateIdRangeFirstValue AND t.SurrogateIdRangeLastValue)
                """)).ShouldBe(0);
            (await database.Repository.GetAsync(new ResourceKey("Patient", "bulk-import-p1"), CancellationToken.None)).ShouldNotBeNull();
            (await database.Repository.GetAsync(new ResourceKey("Patient", "bulk-import-p2"), CancellationToken.None)).ShouldNotBeNull();
            (await database.Repository.GetAsync(new ResourceKey("Observation", "wrong-type"), CancellationToken.None)).ShouldBeNull();

            var search = new SqlServerCompiledSearchService(database.SqlExecutionService, 1,
                new SqlServerSymbolResolver(cache), new CompartmentDefinitionManager(FhirVersion.R4),
                versions.GetSearchParameterDefinitionManager(FhirVersion.R4),
                new GzipResourceCompressor(new RecyclableMemoryStreamManager()), NullLogger.Instance);
            var identifier = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token,
                new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
            var matches = new List<string>();
            await foreach (var match in search.SearchStreamAsync(new SearchOptions
            {
                ResourceType = "Patient",
                Expression = new SearchParameterExpression(identifier,
                    new SearchParameterPredicateExpression(identifier, SearchComparator.Eq, null,
                        new TokenSearchValue("urn:bulk-import-test", "find-me", null)))
            }))
            {
                matches.Add(match.ResourceId);
            }
            matches.ShouldBe(["bulk-import-p1"]);

            var progress = new UpdateProgressActivity(repository, NullLogger<UpdateProgressActivity>.Instance);
            await progress.RunAsync(context, JsonSerializer.Serialize(new[]
            {
                new UpdateProgressInput { JobId = "bulk-import-job", TenantId = 1, ProcessedResources = 2, ProcessedFiles = 1, TotalFiles = 1 }
            }));
            using var services = new ServiceCollection()
                .AddSingleton<IBackgroundJobRepository<ImportJobDefinition>>(repository)
                .AddSingleton<IBlobStorageClient>(blobs)
                .AddSingleton<Microsoft.Extensions.Logging.ILogger<CompleteJobActivity>>(NullLogger<CompleteJobActivity>.Instance)
                .BuildServiceProvider();
            var complete = ActivatorUtilities.CreateInstance<CompleteJobActivity>(services);
            await complete.RunAsync(context, JsonSerializer.Serialize(new[]
            {
                new CompleteJobInput
                {
                    JobId = "bulk-import-job", TenantId = 1, TotalResources = 2, TotalErrors = 1, ErrorLogEntries = output.Errors
                }
            }));

            // Recreate both repository and handler; no in-memory metadata or orchestration history survives.
            var restarted = CreateJobRepository<ImportJobDefinition>(database, tenants);
            var handler = new GetJobStatusHandler(
                new TaskHubClient(new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance)),
                restarted, CreateJobRepository<ExportJobDefinition>(database, tenants));
            var status = await handler.HandleAsync(
                new GetJobStatusQuery { JobId = "bulk-import-job", JobType = "Import", TenantId = 1 }, CancellationToken.None);
            status.Status.ShouldBe("Completed");
            status.ProgressPercentage.ShouldBe(100);
            var result = JsonSerializer.SerializeToNode(status.Result, JsonSerializerOptions.Web)!;
            result["totalResources"]!.GetValue<int>().ShouldBe(2);
            result["totalErrors"]!.GetValue<int>().ShouldBe(1);
            (await blobs.BlobExistsAsync(result["errorFileUrl"]!.GetValue<string>())).ShouldBeTrue();
            (await restarted.GetAsync("bulk-import-job", 2)).ShouldBeNull();
        }
        finally
        {
            await database.DisposeAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
            var legacyErrorLog = Path.Combine("import-errors", "import-errors-bulk-import-job.ndjson");
            if (File.Exists(legacyErrorLog))
            {
                File.Delete(legacyErrorLog);
            }
        }
    }

    private static SqlServerBackgroundJobRepository<T> CreateJobRepository<T>(TestTenantDatabase database, ITenantConfigurationStore tenants)
        where T : class, IJobDefinition =>
        new(database.SqlExecutionService, 1, tenants, NullLogger<SqlServerBackgroundJobRepository<T>>.Instance);

    private sealed class RepositoryFactory(IFhirRepository repository) : IFhirRepositoryFactory
    {
        public Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default) => Task.FromResult(repository);
    }

    private sealed class TenantStore : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = new() { TenantId = 1, DisplayName = "Bulk import test tenant", FhirVersion = "4.0" };
        public TenantMode Mode => TenantMode.Isolated;
        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default) =>
            new(tenantId == 1 ? _tenant : null);
        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default) => new(new[] { _tenant });
        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default) => new((TenantConfiguration?)null);
    }
}
