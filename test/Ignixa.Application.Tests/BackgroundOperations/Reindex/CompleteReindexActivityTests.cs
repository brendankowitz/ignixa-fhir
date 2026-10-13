using System.Text.Json;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class CompleteReindexActivityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenFinalWriteFailsAfterTheLifecycleOutcomeWasAppended_WhenActivityRetries_ThenTheOutcomeIsKeptAndNotAppendedTwice(
        bool orchestrationFailed)
    {
        var fixture = await Fixture.CreateAsync();
        var failWrite = true;
        var jobs = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        jobs.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => fixture.Jobs.GetAsync("job", 1, CancellationToken.None));
        jobs.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(call => failWrite
                ? Task.FromException(new TimeoutException("job store unavailable"))
                : fixture.Jobs.UpdateAsync(call.Arg<BackgroundJob<ReindexJobDefinition>>(), 1, CancellationToken.None));
        var activity = fixture.Activity(jobs: jobs);
        var input = Input([Target()], failureMessage: orchestrationFailed ? "Reindex orchestration failed: worker crashed" : null);

        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(() => activity.RunAsync(ActivityContext(), input));
        (await fixture.JobAsync()).Status.ShouldBe("Running");
        failWrite = false;
        await activity.RunAsync(ActivityContext(), input);

        var job = await fixture.JobAsync();
        if (orchestrationFailed)
        {
            job.Status.ShouldBe("Failed");
            job.ErrorMessage.ShouldContain("worker crashed");
            fixture.Status.ShouldBe(SearchParameterStatus.Pending);
        }
        else
        {
            job.Status.ShouldBe("Completed");
            fixture.Status.ShouldBe(SearchParameterStatus.Enabled);
            fixture.Events.Appended.Select(evt => evt.Data).OfType<SearchParameterReindexCompleted>().ShouldHaveSingleItem()
                .Duration.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromHours(1));
        }
    }

    // Spec 8.5: an active tenant the job did not cover fails the target; a retried append keeps that decision.
    [Fact]
    public async Task GivenActiveTenantWasNotInJobAndTheLifecycleAppendFails_WhenActivityRetries_ThenTheJobFailsWithoutEnablingTarget()
    {
        var fixture = await Fixture.CreateAsync(activeTenants: [Tenant(1), Tenant(2)]);
        var activity = fixture.Activity();

        fixture.Events.FailAppends = true;
        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(
            () => activity.RunAsync(ActivityContext(), Input([Target()])));
        (await fixture.JobAsync()).Status.ShouldBe("Running");
        fixture.Events.FailAppends = false;
        await activity.RunAsync(ActivityContext(), Input([Target()]));

        fixture.Status.ShouldBe(SearchParameterStatus.Pending);
        var job = await fixture.JobAsync();
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("manual $reindex");
    }

    [Theory]
    [InlineData("canonical")]
    [InlineData("resourceType")]
    [InlineData("code")]
    [InlineData("activationEventId")]
    [InlineData("missing")]
    public async Task GivenOwnedTargetIsNotPlannedOrDiffersInOneIdentityField_WhenCompletionRuns_ThenOwnedTargetReturnsToPending(
        string differingField)
    {
        var fixture = await Fixture.CreateAsync();
        var owned = Target();
        ReindexParameterDefinition[] planned = differingField switch
        {
            "canonical" => [owned with { Canonical = Canonical + "-other" }],
            "resourceType" => [owned with { ResourceType = "Observation" }],
            "code" => [owned with { Code = "other" }],
            "activationEventId" => [owned with { ActivationEventId = owned.ActivationEventId + 1 }],
            _ => []
        };

        await fixture.Activity().RunAsync(ActivityContext(), Input(planned));

        var job = await fixture.JobAsync();
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("not planned by this job");
        fixture.Status.ShouldBe(SearchParameterStatus.Pending);
        fixture.State.GetSearchParameter("Patient", "custom")!.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAnOverrideStoredUnderItsBaseCanonical_WhenReindexCompletes_ThenTheBaseCatalogRowIsCheckedAndTheOverrideIsEnabled()
    {
        const string baseCanonical = "http://hl7.org/fhir/SearchParameter/Resource-id";
        var target = Target(overridesCanonical: baseCanonical);
        var fixture = await Fixture.CreateAsync(target: target);

        await fixture.Activity(StoreFactoryWithCatalog(baseCanonical)).RunAsync(
            ActivityContext(),
            Input([target], tenants: Completed(1, failedResourceCount: 153)));

        var job = await fixture.JobAsync();
        job.Status.ShouldBe("Completed");
        ReindexProgress.FromJson(job.Progress)!.Tenants.ShouldHaveSingleItem().FailedResourceCount.ShouldBe(153);
        job.Result!["success"]!.GetValue<bool>().ShouldBeTrue();
        fixture.Status.ShouldBe(SearchParameterStatus.Enabled);
    }

    [Fact]
    public async Task GivenTheTenantCatalogHasNoRowForTheStorageCanonical_WhenCompletionRuns_ThenTheJobFailsAndTheParameterIsNotEnabled()
    {
        var fixture = await Fixture.CreateAsync();

        await fixture.Activity(StoreFactoryWithCatalog()).RunAsync(ActivityContext(), Input([Target()]));

        var job = await fixture.JobAsync();
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain($"Tenant 1: the search-parameter catalog has no row for {Canonical}");
        fixture.Status.ShouldBe(SearchParameterStatus.Pending);
        fixture.State.GetSearchParameter("Patient", "custom")!.ReindexJobId.ShouldBeNull();
    }

    // F2/F11: while a tenant has failed resources of an affected type, the parameter stays out of Enabled.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenATenantWithFailuresOfAnAffectedType_WhenCompletionRuns_ThenTheJobFailsAndTheParameterReturnsToPending(
        bool wholeRangeFailed)
    {
        var fixture = await Fixture.CreateAsync();
        var progress = wholeRangeFailed
            ? Completed(1).AddRangeFailure("Patient", "range timed out")
            : Completed(1).Add("Patient", new ReindexRangeOutput(0, 0, [new ReindexFailedResource("Patient", "p2", "bad")]));

        await fixture.Activity().RunAsync(ActivityContext(), Input([Target()], tenants: progress));

        var job = await fixture.JobAsync();
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("Tenant 1: resource failures occurred");
        fixture.Status.ShouldBe(SearchParameterStatus.Pending);
    }

    // Spec 8.5: the job store's first terminal status is authoritative; a delayed completion changes nothing.
    [Fact]
    public async Task GivenTheJobWasCancelledFirst_WhenCompletionRuns_ThenNeitherTheJobNorItsParameterIsTouched()
    {
        var fixture = await Fixture.CreateAsync();
        var cancelled = await fixture.JobAsync();
        cancelled.Status = "Cancelled";
        cancelled.EndDate = DateTimeOffset.UtcNow;
        await fixture.Jobs.UpdateAsync(cancelled, 1, CancellationToken.None);

        var json = await fixture.Activity().RunAsync(ActivityContext(), Input([Target()]));

        JsonSerializer.Deserialize<CompleteReindexOutput>(json)!.Success.ShouldBeFalse();
        (await fixture.JobAsync()).Status.ShouldBe("Cancelled");
        fixture.Status.ShouldBe(SearchParameterStatus.Reindexing);
        fixture.Events.Appended.Select(evt => evt.Data).ShouldNotContain(data =>
            data is SearchParameterReindexCompleted || data is SearchParameterReindexFailed);
    }

    // Spec 4.4: a target is judged only by tenants of its FHIR version. Another version's active tenant is
    // neither required nor catalog-checked (only tenant 1 has a store), and its failures do not decide it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAnActiveTenantOnAnotherFhirVersion_WhenAnR4TargetCompletes_ThenThatTenantDoesNotDecideIt(
        bool otherTenantRanAndFailed)
    {
        var target = Target(fhirVersion: "4.0");
        var fixture = await Fixture.CreateAsync(
            activeTenants: [Tenant(1), Tenant(2, fhirVersion: "5.0")],
            target: target,
            jobTargets: [target]);
        ReindexTenantProgress[] tenants = otherTenantRanAndFailed
            ? [Completed(1), Completed(2).AddRangeFailure("Patient", "range timed out")]
            : [Completed(1)];

        await fixture.Activity().RunAsync(ActivityContext(), Input([target], tenants: tenants));

        fixture.Status.ShouldBe(SearchParameterStatus.Enabled);
    }

    private static ReindexTenantProgress Completed(int tenantId, long failedResourceCount = 0) =>
        new(tenantId, ReindexTenantStatus.Completed, 1, 1, 1, 1, 0, failedResourceCount, null);

    private static string Input(
        ReindexParameterDefinition[] targets,
        string? failureMessage = null,
        params ReindexTenantProgress[] tenants) =>
        JsonSerializer.Serialize(new[]
        {
            new CompleteReindexInput("job", 1, targets, tenants.Length == 0 ? [Completed(1)] : tenants, [])
            {
                FailureMessage = failureMessage
            }
        });

    private static IReindexStoreFactory StoreFactoryWithCatalog(params string[] storageCanonicals)
    {
        var store = Substitute.For<IReindexStore>();
        store.HasSearchParameterAsync(Arg.Is<string>(uri => storageCanonicals.Contains(uri)), Arg.Any<CancellationToken>())
            .Returns(true);
        var storeFactory = Substitute.For<IReindexStoreFactory>();
        storeFactory.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(store);
        return storeFactory;
    }

    /// <summary>A running job that started an hour ago and owns <c>Patient.custom</c> on tenant 1.</summary>
    private sealed class Fixture
    {
        private readonly ITenantConfigurationStore _tenants;

        private Fixture(ITenantConfigurationStore tenants, ReindexParameterDefinition target)
        {
            _tenants = tenants;
            Jobs = JobRepository(tenants);
            State.ApplyAndTrack(Activation(
                overrides: new OverrideInfo("http://hl7.org/fhir/SearchParameter/Resource-id", 17),
                fhirVersion: target.FhirVersion));
            Lifecycle = new ReindexLifecycleEventWriter(Events, State);
        }

        public InMemoryBackgroundJobRepository<ReindexJobDefinition> Jobs { get; }

        public ConformanceState State { get; } = new();

        public TestSourceEventStore Events { get; } = new();

        public ReindexLifecycleEventWriter Lifecycle { get; }

        public SearchParameterStatus Status => State.GetSearchParameter("Patient", "custom")!.Status;

        public static async Task<Fixture> CreateAsync(
            TenantConfiguration[]? activeTenants = null,
            ReindexParameterDefinition? target = null,
            ReindexParameterDefinition[]? jobTargets = null)
        {
            target ??= Target();
            var fixture = new Fixture(TenantStore(activeTenants ?? [Tenant(1)]), target);
            var job = Job("Running", definition: CreateJobDefinition(jobTargets));
            job.StartDate = DateTimeOffset.UtcNow.AddHours(-1);
            await fixture.Jobs.CreateAsync(job, CancellationToken.None);
            await fixture.Lifecycle.StartAsync("job", [target], CancellationToken.None);
            return fixture;
        }

        public async Task<BackgroundJob<ReindexJobDefinition>> JobAsync() =>
            (await Jobs.GetAsync("job", 1, CancellationToken.None))!;

        public CompleteReindexActivity Activity(
            IReindexStoreFactory? storeFactory = null,
            IBackgroundJobRepository<ReindexJobDefinition>? jobs = null) =>
            new(
                storeFactory ?? StoreFactoryWithCatalog(Canonical),
                Lifecycle,
                jobs ?? Jobs,
                new TestReindexJobLock(),
                _tenants,
                TimeProvider.System,
                NullLogger<CompleteReindexActivity>.Instance);
    }
}
