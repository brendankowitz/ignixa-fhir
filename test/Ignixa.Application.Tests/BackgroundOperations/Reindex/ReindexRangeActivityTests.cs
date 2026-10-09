using System.Text.Json;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexRangeActivityTests
{
    [Fact]
    public async Task GivenConcurrentRangeCompletions_WhenExecuted_ThenNoSingletonLockIsAcquired()
    {
        var (activity, jobLock) = CreateActivity(failProgress: false, concurrentReaders: 32);

        var outputs = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            activity.RunAsync(null!, JsonSerializer.Serialize(
                new[] { new ReindexRangeInput("job", 1, "Patient", 1, 10, 42, 10, 0) }))));

        outputs.Length.ShouldBe(32);
        jobLock.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GivenProgressStorageFails_WhenRangeFinishes_ThenRangeStillReturnsItsCounts()
    {
        var (activity, _) = CreateActivity(failProgress: true);

        var result = await activity.RunAsync(null!, JsonSerializer.Serialize(
            new[] { new ReindexRangeInput("job", 1, "Patient", 1, 10, 42, 10, 0) }));

        JsonSerializer.Deserialize<ReindexRangeOutput>(result)!.ResourcesRead.ShouldBe(0);
    }

    private static (ReindexRangeActivity Activity, CountingJobLock JobLock) CreateActivity(
        bool failProgress, int concurrentReaders = 1)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Tenant", FhirVersion = "4.0" });
        var store = Substitute.For<IFhirRepository, IReindexStore>();
        var readers = 0;
        var readGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IReindexStore)store).ReadRangeAsync(
                Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(),
                Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (Interlocked.Increment(ref readers) == concurrentReaders)
                {
                    readGate.TrySetResult();
                }

                await readGate.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return (IReadOnlyList<ReindexResource>)Array.Empty<ReindexResource>();
            });
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(store);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(), Substitute.For<IFhirSchemaProvider>(), 42));
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>()).Returns(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job", JobType = 4, Status = "Running", Definition = ReindexTestHelper.CreateJobDefinition()
        });
        if (failProgress)
        {
            repository.TryUpdateProgressAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromException<bool>(new TimeoutException("progress storage unavailable")));
        }

        var jobLock = new CountingJobLock();
        var progress = new ReindexProgressReporter(new ReindexJobUpdater(
            repository, jobLock, Substitute.For<IReindexCompletionHook>()));
        var heartbeat = new ReindexActivityHeartbeat(
            progress, Options.Create(new ReindexOptions()), TimeProvider.System,
            NullLogger<ReindexActivityHeartbeat>.Instance);
        return (new ReindexRangeActivity(
            new ReindexRangeProcessor(
                repositories,
                tenants,
                versions,
                Substitute.For<IConformanceDefinitionsSynchronizer>(),
                new FhirRequestContextAccessor()),
            heartbeat), jobLock);
    }

    private sealed class CountingJobLock : IReindexJobLock
    {
        private int _calls;
        public int Calls => _calls;

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return action(cancellationToken);
        }
    }
}
