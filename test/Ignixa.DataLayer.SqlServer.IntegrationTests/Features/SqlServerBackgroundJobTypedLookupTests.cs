using System.Text.Json;
using Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features;

/// <summary>
/// The typed lookup <c>$bulk-delete</c> status and cancel depend on: a job ID belongs to one job type, and
/// a lookup for another type must report it absent instead of deserializing its definition into the
/// wrong shape.
/// </summary>
public class SqlServerBackgroundJobTypedLookupTests : IAsyncLifetime
{
    private const int OwnerTenantId = 7;
    private const int OtherTenantId = 9;

    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenAnExportJob_WhenLookedUpAsABulkDeleteJob_ThenItIsAbsentRatherThanMisread()
    {
        var jobId = Guid.NewGuid().ToString();
        await Repository<ExportJobDefinition>().CreateAsync(ExportJob(jobId), CancellationToken.None);
        var bulkDeletes = Repository<BulkDeleteJobDefinition>();

        var typed = await bulkDeletes.GetAsync(jobId, OwnerTenantId, (int)BackgroundJobType.BulkDelete, CancellationToken.None);

        typed.ShouldBeNull();
        // The untyped lookup is why the typed one exists: it deserializes the export definition into the
        // bulk-delete shape and fails on its required members.
        await Should.ThrowAsync<JsonException>(() => bulkDeletes.GetAsync(jobId, OwnerTenantId, CancellationToken.None));
    }

    [Fact]
    public async Task GivenABulkDeleteJob_WhenLookedUpWithItsType_ThenItIsReturned()
    {
        var jobId = Guid.NewGuid().ToString();
        var repository = Repository<BulkDeleteJobDefinition>();
        await repository.CreateAsync(BulkDeleteJob(jobId), CancellationToken.None);

        var job = await repository.GetAsync(jobId, OwnerTenantId, (int)BackgroundJobType.BulkDelete, CancellationToken.None);

        job.ShouldNotBeNull();
        job.JobType.ShouldBe((int)BackgroundJobType.BulkDelete);
        job.Definition.ResourceTypes.ShouldBe(["Patient"]);
        job.Definition.Mode.ShouldBe(BulkDeleteMode.HardDelete);
    }

    [Theory]
    [InlineData(TenantMode.Isolated, false)]
    [InlineData(TenantMode.Distributed, true)]
    public async Task GivenAnotherTenantsJob_WhenLookedUpWithItsType_ThenTenantValidationMatchesTheUntypedLookup(
        TenantMode mode, bool visible)
    {
        var jobId = Guid.NewGuid().ToString();
        var repository = Repository<BulkDeleteJobDefinition>(mode);
        await repository.CreateAsync(BulkDeleteJob(jobId), CancellationToken.None);

        var job = await repository.GetAsync(jobId, OtherTenantId, (int)BackgroundJobType.BulkDelete, CancellationToken.None);

        (job is not null).ShouldBe(visible);
    }

    private SqlServerBackgroundJobRepository<T> Repository<T>(TenantMode mode = TenantMode.Isolated)
        where T : class, IJobDefinition =>
        new(_database.SqlExecutionService, _database.TenantId, new ModeOnlyTenantStore(mode),
            NullLogger<SqlServerBackgroundJobRepository<T>>.Instance);

    private static BackgroundJob<ExportJobDefinition> ExportJob(string jobId) => new()
    {
        JobId = jobId,
        JobType = (int)BackgroundJobType.Export,
        Status = "Running",
        Definition = new ExportJobDefinition
        {
            TenantId = OwnerTenantId,
            ResourceTypes = ["Patient"],
            TypeFilters = new Dictionary<string, string>(),
            OutputFormat = "ndjson",
            OutputPath = "/exports/test",
        },
    };

    private static BackgroundJob<BulkDeleteJobDefinition> BulkDeleteJob(string jobId) => new()
    {
        JobId = jobId,
        JobType = (int)BackgroundJobType.BulkDelete,
        Status = "Queued",
        Definition = new BulkDeleteJobDefinition
        {
            TenantId = OwnerTenantId,
            ResourceTypes = ["Patient"],
            SearchQuery = string.Empty,
            Mode = BulkDeleteMode.HardDelete,
            ExcludedResourceTypes = [],
        },
    };

    private sealed class ModeOnlyTenantStore(TenantMode mode) : ITenantConfigurationStore
    {
        public TenantMode Mode => mode;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => ValueTask.FromResult<TenantConfiguration?>(null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<TenantConfiguration>>([]);

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TenantConfiguration?>(null);
    }
}
