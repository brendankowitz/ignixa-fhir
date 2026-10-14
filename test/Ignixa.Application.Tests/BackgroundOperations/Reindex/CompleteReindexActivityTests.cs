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
    // §8.5: the job is written after the idempotent lifecycle append; the catalog is checked by storage canonical.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenTheAppendAndThenTheFinalWriteFail_WhenActivityRetries_ThenTheOutcomeIsKeptAndNotAppendedTwice(
        bool orchestrationFailed)
    {
        const string baseCanonical = "http://hl7.org/fhir/SearchParameter/Resource-id";
        var target = Target(overridesCanonical: baseCanonical);
        var fixture = await Fixture.CreateAsync(target: target);
        var failWrite = true;
        var jobs = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        jobs.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => fixture.Jobs.GetAsync("job", 1, CancellationToken.None));
        jobs.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(call => failWrite
                ? Task.FromException(new TimeoutException("job store unavailable"))
                : fixture.Jobs.UpdateAsync(call.Arg<BackgroundJob<ReindexJobDefinition>>(), 1, CancellationToken.None));
        var activity = fixture.Activity(StoreFactoryWithCatalog(baseCanonical), jobs);
        var input = Input(
            [target],
            orchestrationFailed ? "Reindex orchestration failed: worker crashed" : null,
            Completed(1, failedResourceCount: 153));

        fixture.Events.FailAppends = true;
        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(() => activity.RunAsync(ActivityContext(), input));
        (await fixture.JobAsync()).Status.ShouldBe("Running");
        fixture.Events.FailAppends = false;
        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(() => activity.RunAsync(ActivityContext(), input));
        (await fixture.JobAsync()).Status.ShouldBe("Running");
        failWrite = false;
        await activity.RunAsync(ActivityContext(), input);

        var job = await fixture.JobAsync();
        job.Result!["success"]!.GetValue<bool>().ShouldBe(!orchestrationFailed);
        ReindexProgress.FromJson(job.Progress)!.Tenants.ShouldHaveSingleItem().FailedResourceCount.ShouldBe(153);
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

    // §8.5 (M7): an owned target that does not match a planned identity fails back to Pending.
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
        // §4.3: a planned target the job does not own is reported as an ignored lifecycle event.
        ReindexProgress.FromJson(job.Progress)!.IgnoredLifecycleEvents.Count.ShouldBe(differingField == "missing" ? 0 : 1);
        fixture.Status.ShouldBe(SearchParameterStatus.Pending);
        fixture.State.GetSearchParameter("Patient", "custom")!.ReindexJobId.ShouldBeNull();
    }

    // §8.5, F2/F11: an uncovered tenant, a missing catalog row or failures of an affected type fail the target.
    [Theory]
    [InlineData("tenantAddedAfterStart", "a manual $reindex is needed", ReindexTenantStatus.Completed)]
    [InlineData("noCatalogRow", "Tenant 1: the search-parameter catalog has no row for " + Canonical, ReindexTenantStatus.Completed)]
    [InlineData("failedResource", "Tenant 1: resource failures occurred", ReindexTenantStatus.Failed)]
    [InlineData("failedRange", "Tenant 1: resource failures occurred", ReindexTenantStatus.Failed)]
    public async Task GivenATenantThatCannotEnableTheTarget_WhenCompletionRuns_ThenTheJobFailsAndTheParameterReturnsToPending(
        string blocker, string expectedError, ReindexTenantStatus expectedTenantStatus)
    {
        var fixture = await Fixture.CreateAsync(activeTenants: blocker == "tenantAddedAfterStart" ? [Tenant(1), Tenant(2)] : null);
        var progress = blocker switch
        {
            "failedRange" => Completed(1).AddRangeFailure("Patient", "range timed out"),
            "failedResource" => Completed(1).Add("Patient", new ReindexRangeOutput(0, 0, [new ReindexFailedResource("Patient", "p2", "bad")])),
            _ => Completed(1)
        };

        await fixture.Activity(blocker == "noCatalogRow" ? StoreFactoryWithCatalog() : null)
            .RunAsync(ActivityContext(), Input([Target()], tenants: progress));

        var job = await fixture.JobAsync();
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain(expectedError);
        ReindexProgress.FromJson(job.Progress)!.Tenants.ShouldHaveSingleItem().Status.ShouldBe(expectedTenantStatus);
        fixture.Status.ShouldBe(SearchParameterStatus.Pending);
        fixture.State.GetSearchParameter("Patient", "custom")!.ReindexJobId.ShouldBeNull();
    }

    // Spec §8.5: the job store's first terminal status is authoritative; a delayed completion changes nothing.
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

    // Spec §4.4, §8.6: another FHIR version's tenant neither decides an R4 target nor escapes failing the job.
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
        (await fixture.JobAsync()).Status.ShouldBe(otherTenantRanAndFailed ? "Failed" : "Completed");
    }

    private static ReindexTenantProgress Completed(int tenantId, long failedResourceCount = 0) =>
        new(tenantId, ReindexTenantStatus.Completed, 1, 1, 1, 1, 0, failedResourceCount, null);

    private static string Input(
        ReindexParameterDefinition[] targets, string? failureMessage = null, params ReindexTenantProgress[] tenants) =>
        JsonSerializer.Serialize(new[]
        {
            new CompleteReindexInput("job", 1, targets, tenants.Length == 0 ? [Completed(1)] : tenants, []) { FailureMessage = failureMessage }
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
            TenantConfiguration[]? activeTenants = null, ReindexParameterDefinition? target = null, ReindexParameterDefinition[]? jobTargets = null)
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
            IReindexStoreFactory? storeFactory = null, IBackgroundJobRepository<ReindexJobDefinition>? jobs = null) =>
            new(storeFactory ?? StoreFactoryWithCatalog(Canonical), Lifecycle, jobs ?? Jobs, new TestReindexJobLock(), _tenants,
                TimeProvider.System, NullLogger<CompleteReindexActivity>.Instance);
    }
}
