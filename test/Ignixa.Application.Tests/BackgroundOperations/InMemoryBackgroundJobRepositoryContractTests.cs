using System.Text.Json.Nodes;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class InMemoryBackgroundJobRepositoryContractTests
{
    private const string ConflictType = "Ignixa.Domain.Exceptions.BackgroundJobUpdateConflictException";

    [Fact]
    public async Task GivenCompletionAfterTheStoredEntryIsRead_WhenTheStaleUpdateResumes_ThenItConflicts()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var pauseNextValidation = 0;
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(_ =>
        {
            if (Interlocked.Exchange(ref pauseNextValidation, 0) == 1)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The test did not release the paused ownership validation.");
                }
            }

            return TenantMode.Isolated;
        });
        var repository = new InMemoryBackgroundJobRepository<ExportJobDefinition>(
            tenants, NullLogger<InMemoryBackgroundJobRepository<ExportJobDefinition>>.Instance);
        var job = ExportJob();
        await repository.CreateAsync(job, CancellationToken.None);
        var stale = ExportJob(job.JobId);
        Interlocked.Exchange(ref pauseNextValidation, 1);
        var update = Task.Run(() => Record.ExceptionAsync(() => repository.UpdateAsync(stale, 1, CancellationToken.None)));

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var terminal = ExportJob(job.JobId);
            terminal.Status = "Completed";
            terminal.Result = JsonNode.Parse("""{"files":["winner.ndjson"]}""");
            await repository.UpdateAsync(terminal, 1, CancellationToken.None);
            release.Set();

            var conflict = await update;
            conflict.ShouldNotBeNull();
            conflict.GetType().FullName.ShouldBe(ConflictType);
            var stored = (await repository.GetAsync(job.JobId, 1, CancellationToken.None))!;
            stored.Status.ShouldBe("Completed");
            stored.Result!["files"]![0]!.GetValue<string>().ShouldBe("winner.ndjson");
        }
        finally
        {
            release.Set();
            await update;
        }
    }

    [Theory]
    [InlineData("Completed", "Running")]
    [InlineData("Completed", "Completed")]
    [InlineData("Completed", "Cancelled")]
    [InlineData("Failed", "Running")]
    [InlineData("Failed", "Completed")]
    [InlineData("Cancelled", "Running")]
    [InlineData("Cancelled", "Completed")]
    public async Task GivenFirstTerminalMetadata_WhenALateWriteArrives_ThenItConflictsWithoutChangingStoredData(
        string terminalStatus, string staleStatus)
    {
        var repository = CreateRepository<ExportJobDefinition>();
        var job = ExportJob();
        await repository.CreateAsync(job, CancellationToken.None);
        var stale = (await repository.GetAsync(job.JobId, 1, CancellationToken.None))!;
        // Separate input ensures the original implementation cannot make the test tautological by aliasing.
        var terminal = ExportJob(job.JobId);
        terminal.Status = terminalStatus;
        terminal.EndDate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        terminal.ErrorMessage = "first terminal detail";
        terminal.Result = JsonNode.Parse("""{"outputFiles":{"Patient":["winner.ndjson"]},"totalResources":42}""");
        await repository.UpdateAsync(terminal, 1, CancellationToken.None);
        stale.Status = staleStatus;
        stale.Result = null;
        stale.EndDate = null;
        stale.Progress!["nested"]!["count"] = 999;
        stale.CancelRequested = true;

        var conflict = await Should.ThrowAsync<Exception>(() => repository.UpdateAsync(stale, 1, CancellationToken.None));

        conflict.GetType().FullName.ShouldBe(ConflictType);
        var current = (await repository.GetAsync(job.JobId, 1, CancellationToken.None))!;
        current.Status.ShouldBe(terminalStatus);
        current.EndDate.ShouldBe(terminal.EndDate);
        current.ErrorMessage.ShouldBe("first terminal detail");
        current.Result!.ToJsonString().ShouldBe(terminal.Result.ToJsonString());
        current.Progress!["nested"]!["count"]!.GetValue<int>().ShouldBe(1);
        current.CancelRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAnExportJob_WhenTheCreateInputIsMutated_ThenStoredMetadataAndDefinitionAreDetached()
    {
        var repository = CreateRepository<ExportJobDefinition>();
        var job = ExportJob();
        await repository.CreateAsync(job, CancellationToken.None);

        MutateExport(job);

        AssertOriginalExport((await repository.GetAsync(job.JobId, 1, CancellationToken.None))!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAnExportJob_WhenAReadOrListSnapshotIsMutated_ThenStoredDataIsUnchanged(bool list)
    {
        var repository = CreateRepository<ExportJobDefinition>();
        var job = ExportJob();
        await repository.CreateAsync(job, CancellationToken.None);
        var snapshot = list ? (await repository.ListAsync()).Single()
            : (await repository.GetAsync(job.JobId, 1, CancellationToken.None))!;

        MutateExport(snapshot);

        AssertOriginalExport((await repository.GetAsync(job.JobId, 1, CancellationToken.None))!);
    }

    [Fact]
    public async Task GivenAnAcceptedUpdate_WhenItsInputIsMutatedAgain_ThenStoredDataRetainsTheAcceptedSnapshot()
    {
        var repository = CreateRepository<ExportJobDefinition>();
        var job = ExportJob();
        await repository.CreateAsync(job, CancellationToken.None);
        var updated = ExportJob(job.JobId);
        await repository.UpdateAsync(updated, 1, CancellationToken.None);

        MutateExport(updated);

        AssertOriginalExport((await repository.GetAsync(job.JobId, 1, CancellationToken.None))!);
    }

    [Fact]
    public async Task GivenAnImportDefinition_WhenInputAndReturnedFilesAreMutated_ThenStoredDefinitionIsDetached()
    {
        var repository = CreateRepository<ImportJobDefinition>();
        var files = new List<InputFileInfo> { new() { Type = "Patient", Url = "input.ndjson", ETag = "original" } };
        var job = new BackgroundJob<ImportJobDefinition>
        {
            JobId = Guid.NewGuid().ToString(), JobType = 2, Status = "Running",
            Definition = new ImportJobDefinition
            {
                TenantId = 1, InputFormat = "application/fhir+ndjson", InputSource = "Patient",
                Mode = "IncrementalLoad", InputFiles = files,
            },
        };
        await repository.CreateAsync(job, CancellationToken.None);
        files[0] = new InputFileInfo { Type = "Observation", Url = "changed.ndjson" };
        var fetched = (await repository.GetAsync(job.JobId, 1, CancellationToken.None))!;
        fetched.Definition.InputFiles.Single().Url.ShouldBe("input.ndjson");
        ((IList<InputFileInfo>)fetched.Definition.InputFiles)[0] = new InputFileInfo { Type = "Patient", Url = "another.ndjson" };

        var current = (await repository.GetAsync(job.JobId, 1, CancellationToken.None))!;
        current.Definition.InputFiles.Single().ETag.ShouldBe("original");
        current.Definition.InputFiles.Single().Url.ShouldBe("input.ndjson");
        current.Definition.InputFormat.ShouldBe("application/fhir+ndjson");
    }

    [Theory]
    [InlineData(TenantMode.Isolated, 1)]
    [InlineData(TenantMode.Distributed, 2)]
    public async Task GivenAnAuthorizedNonterminalJob_WhenUpdated_ThenTheChangeIsAccepted(TenantMode mode, int requester)
    {
        var repository = CreateRepository<ExportJobDefinition>(mode);
        var job = ExportJob();
        await repository.CreateAsync(job, CancellationToken.None);
        var updated = ExportJob(job.JobId);
        updated.Progress!["nested"]!["count"] = 2;

        await repository.UpdateAsync(updated, requester, CancellationToken.None);

        (await repository.GetAsync(job.JobId, 1, CancellationToken.None))!.Progress!["nested"]!["count"]!.GetValue<int>().ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnIsolatedOwnersJob_WhenAnotherTenantSpoofsTheReplacement_ThenTheStoredOwnerRemainsAuthorized()
    {
        var repository = CreateRepository<ExportJobDefinition>();
        var job = ExportJob();
        await repository.CreateAsync(job, CancellationToken.None);
        var spoofed = ExportJob(job.JobId, 2);

        await Should.ThrowAsync<InvalidOperationException>(() => repository.UpdateAsync(spoofed, 2, CancellationToken.None));

        (await repository.GetAsync(job.JobId, 1, CancellationToken.None)).ShouldNotBeNull();
        (await repository.GetAsync(job.JobId, 2, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenDistributedMode_WhenAnUpdateChangesOwner_ThenItIsRejected()
    {
        var repository = CreateRepository<ExportJobDefinition>(TenantMode.Distributed);
        var job = ExportJob();
        await repository.CreateAsync(job, CancellationToken.None);

        await Should.ThrowAsync<InvalidOperationException>(() => repository.UpdateAsync(ExportJob(job.JobId, 2), 2, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNoStoredJob_WhenUpdating_ThenTheFailureIsNotATerminalConflict()
    {
        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateRepository<ExportJobDefinition>().UpdateAsync(ExportJob(), 1, CancellationToken.None));
        error.GetType().FullName.ShouldNotBe(ConflictType);
    }

    private static InMemoryBackgroundJobRepository<T> CreateRepository<T>(TenantMode mode = TenantMode.Isolated)
        where T : class, IJobDefinition
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(mode);
        return new(tenants, NullLogger<InMemoryBackgroundJobRepository<T>>.Instance);
    }

    private static BackgroundJob<ExportJobDefinition> ExportJob(string? id = null, int tenantId = 1) => new()
    {
        JobId = id ?? Guid.NewGuid().ToString(), JobType = 1, Status = "Running",
        Definition = new ExportJobDefinition
        {
            TenantId = tenantId, ResourceTypes = new List<string> { "Patient" },
            TypeFilters = new Dictionary<string, string> { ["Patient"] = "active=true" },
            OutputFormat = "ndjson", OutputPath = "/exports/snapshots",
        },
        Progress = JsonNode.Parse("""{"nested":{"count":1}}"""),
        Result = JsonNode.Parse("""{"files":["original.ndjson"]}"""),
    };

    private static void MutateExport(BackgroundJob<ExportJobDefinition> job)
    {
        job.Status = "Completed";
        ((IList<string>)job.Definition.ResourceTypes)[0] = "Observation";
        ((IDictionary<string, string>)job.Definition.TypeFilters)["Patient"] = "active=false";
        job.Progress!["nested"]!["count"] = 999;
        job.Result!["files"]![0] = "changed.ndjson";
    }

    private static void AssertOriginalExport(BackgroundJob<ExportJobDefinition> job)
    {
        job.Status.ShouldBe("Running");
        job.Definition.ResourceTypes.ShouldBe(["Patient"]);
        job.Definition.TypeFilters["Patient"].ShouldBe("active=true");
        job.Definition.OutputPath.ShouldBe("/exports/snapshots");
        job.Progress!["nested"]!["count"]!.GetValue<int>().ShouldBe(1);
        job.Result!["files"]![0]!.GetValue<string>().ShouldBe("original.ndjson");
    }
}
