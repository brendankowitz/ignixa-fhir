using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.DataLayer.BlobStorage.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ExportCompletion = Ignixa.Application.BackgroundOperations.Export.Activities.CompleteJobActivity;
using ExportCompletionInput = Ignixa.Application.BackgroundOperations.Export.Activities.CompleteJobInput;
using ImportCompletion = Ignixa.Application.BackgroundOperations.Import.Activities.CompleteJobActivity;
using ImportProgress = Ignixa.Application.BackgroundOperations.Import.Activities.UpdateProgressActivity;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class BulkJobSupersededActivityTests : IAsyncLifetime
{
    private readonly string _directory = Path.GetFullPath(Path.Combine("bulk-activity-conflict-blobs", Guid.NewGuid().ToString("N")));
    private readonly InMemoryBackgroundJobRepository<ImportJobDefinition> _imports;
    private readonly InMemoryBackgroundJobRepository<ExportJobDefinition> _exports;
    private readonly LocalFileBlobClient _blobs;
    private readonly TaskContext _context = new(new OrchestrationInstance { InstanceId = "job" });

    public BulkJobSupersededActivityTests()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        _imports = new(tenants, NullLogger<InMemoryBackgroundJobRepository<ImportJobDefinition>>.Instance);
        _exports = new(tenants, NullLogger<InMemoryBackgroundJobRepository<ExportJobDefinition>>.Instance);
        _blobs = new(Options.Create(new LocalFileBlobStorageOptions { RootDirectory = _directory }),
            NullLogger<LocalFileBlobClient>.Instance);
    }

    public async Task InitializeAsync()
    {
        await _imports.CreateAsync(new BackgroundJob<ImportJobDefinition>
        {
            JobId = "job", JobType = 2, Status = "Running",
            Definition = new ImportJobDefinition
            {
                TenantId = 1, InputFormat = "application/fhir+ndjson", InputSource = "Patient", Mode = "IncrementalLoad",
                InputFiles = [new InputFileInfo { Type = "Patient", Url = "input.ndjson" }]
            }
        }, CancellationToken.None);
        await _exports.CreateAsync(new BackgroundJob<ExportJobDefinition>
        {
            JobId = "job", JobType = 1, Status = "Running",
            Definition = new ExportJobDefinition
            {
                TenantId = 1, ResourceTypes = ["Patient"], TypeFilters = new Dictionary<string, string>(),
                OutputFormat = "application/fhir+ndjson", OutputPath = "partition/1/export/job"
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task GivenCompletionAfterProgressRead_WhenUpdatingProgress_ThenTerminalResultRemains()
    {
        var repository = ForwardImports();
        repository.UpdateAsync(Arg.Any<BackgroundJob<ImportJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var winner = (await _imports.GetAsync("job", 1, CancellationToken.None))!;
                winner.Status = "Completed";
                winner.Result = JsonNode.Parse("""{"TotalResources":7,"TotalErrors":0}""");
                winner.Progress = JsonNode.Parse("""{"ProcessedResources":7,"ProgressPercentage":100}""");
                await _imports.UpdateAsync(winner, 1, CancellationToken.None);
                await _imports.UpdateAsync(call.Arg<BackgroundJob<ImportJobDefinition>>(), 1, CancellationToken.None);
            });
        var activity = new ImportProgress(repository, NullLogger<ImportProgress>.Instance);

        var output = await RunAsync<bool>(activity, Progress());

        output.ShouldBeFalse();
        var stored = (await _imports.GetAsync("job", 1, CancellationToken.None))!;
        stored.Status.ShouldBe("Completed");
        stored.Result!["TotalResources"]!.GetValue<int>().ShouldBe(7);
        stored.Progress!["ProgressPercentage"]!.GetValue<int>().ShouldBe(100);
    }

    [Fact]
    public async Task GivenStorageFailure_WhenUpdatingProgress_ThenItIsNotSuppressedAsSupersededWork()
    {
        var repository = ForwardImports();
        repository.UpdateAsync(Arg.Any<BackgroundJob<ImportJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("storage outage"));
        var activity = new ImportProgress(repository, NullLogger<ImportProgress>.Instance);

        var failure = await Should.ThrowAsync<Exception>(() => RunAsync<bool>(activity, Progress()));

        failure.ToString().ShouldContain("storage outage");
    }

    [Theory]
    [InlineData("Completed", true)]
    [InlineData("Failed", false)]
    [InlineData("Cancelled", false)]
    public async Task GivenTerminalWinnerAfterExportRead_WhenCompletingExport_ThenAuthoritativeOutcomeIsUsed(
        string terminalStatus, bool succeeded)
    {
        var repository = Substitute.For<IBackgroundJobRepository<ExportJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => _exports.GetAsync("job", 1, CancellationToken.None));
        repository.UpdateAsync(Arg.Any<BackgroundJob<ExportJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var winner = (await _exports.GetAsync("job", 1, CancellationToken.None))!;
                winner.Status = terminalStatus;
                winner.Result = JsonNode.Parse("""{"TotalResources":7,"ExportedFiles":{"Patient":"winning.ndjson"}}""");
                await _exports.UpdateAsync(winner, 1, CancellationToken.None);
                await _exports.UpdateAsync(call.Arg<BackgroundJob<ExportJobDefinition>>(), 1, CancellationToken.None);
            });
        var activity = new ExportCompletion(repository, NullLogger<ExportCompletion>.Instance);

        var output = await RunAsync<bool>(activity,
            new ExportCompletionInput("job", 1, true, new Dictionary<string, string> { ["Patient"] = "stale.ndjson" }, 99, null));

        output.ShouldBe(succeeded);
        var stored = (await _exports.GetAsync("job", 1, CancellationToken.None))!;
        stored.Status.ShouldBe(terminalStatus);
        stored.Result!["TotalResources"]!.GetValue<int>().ShouldBe(7);
        stored.Result["ExportedFiles"]!["Patient"]!.GetValue<string>().ShouldBe("winning.ndjson");
    }

    [Fact]
    public async Task GivenCompletedImport_WhenCompletionIsReplayed_ThenItsResultAndArtifactAreUnchanged()
    {
        var activity = CreateImportCompletion(_imports);
        var winner = await RunAsync<CompleteJobOutput>(activity, Completion("winning error", 7));
        var original = await ReadBlobAsync(winner.ErrorFileUrl!);

        var replay = await RunAsync<CompleteJobOutput>(activity, Completion("stale error", 99));

        replay.Result!.TotalResources.ShouldBe(7);
        replay.ErrorFileUrl.ShouldBe(winner.ErrorFileUrl);
        (await ReadBlobAsync(winner.ErrorFileUrl!)).ShouldBe(original);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenTerminalImport_WhenLateCompletionArrives_ThenItsActualStateIsReturned(string status)
    {
        var winner = (await _imports.GetAsync("job", 1, CancellationToken.None))!;
        winner.Status = status;
        winner.ErrorMessage = status == "Failed" ? "winning failure" : null;
        winner.Result = status == "Cancelled" ? null : JsonNode.Parse("""{"TotalResources":7,"TotalErrors":0}""");
        await _imports.UpdateAsync(winner, 1, CancellationToken.None);

        var json = await CreateImportCompletion(_imports).RunAsync(_context,
            JsonSerializer.Serialize(new[] { Completion("stale error", 99) }));
        var output = JsonNode.Parse(json)!;

        output["Status"]!.GetValue<string>().ShouldBe(status);
        if (status == "Cancelled")
        {
            output["Result"].ShouldBeNull();
        }
        else
        {
            output["Result"]!["TotalResources"]!.GetValue<int>().ShouldBe(7);
        }
        Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCompletionAfterImportRead_WhenPublishingErrors_ThenLosingAttemptCannotReplaceWinnerArtifact()
    {
        var repository = ForwardImports();
        CompleteJobOutput? winner = null;
        string? original = null;
        var reads = 0;
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var snapshot = await _imports.GetAsync("job", 1, CancellationToken.None);
            if (++reads == 1)
            {
                winner = await RunAsync<CompleteJobOutput>(CreateImportCompletion(_imports), Completion("winning error", 7));
                original = await ReadBlobAsync(winner.ErrorFileUrl!);
            }
            return snapshot;
        });

        var output = await RunAsync<CompleteJobOutput>(CreateImportCompletion(repository), Completion("stale error", 99));

        output.Result!.TotalResources.ShouldBe(7);
        output.ErrorFileUrl.ShouldBe(winner!.ErrorFileUrl);
        (await ReadBlobAsync(winner.ErrorFileUrl!)).ShouldBe(original);
        Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Length.ShouldBe(1);
        var stored = (await _imports.GetAsync("job", 1, CancellationToken.None))!;
        stored.Result!["TotalResources"]!.GetValue<int>().ShouldBe(7);
    }

    [Fact]
    public async Task GivenStorageFailure_WhenCompletingImport_ThenItIsNotReplacedByAnAuthoritativeFallback()
    {
        var repository = ForwardImports();
        repository.UpdateAsync(Arg.Any<BackgroundJob<ImportJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("completion storage outage"));

        var failure = await Should.ThrowAsync<Exception>(() =>
            RunAsync<CompleteJobOutput>(CreateImportCompletion(repository), Completion("error", 1)));

        failure.ToString().ShouldContain("completion storage outage");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("input file failed")]
    public async Task GivenUnavailableErrorStorage_WhenFinalizingImport_ThenFailedMetadataDoesNotDependOnAnArtifact(string? originalFailure)
    {
        var storage = Substitute.For<IBlobStorageClient>();
        storage.WriteBlobAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("blob storage unavailable"));

        var output = await RunAsync<CompleteJobOutput>(
            CreateImportCompletion(_imports, storage),
            Completion("rejected record", 2) with { ErrorMessage = originalFailure });

        output.Status.ShouldBe("Failed");
        output.ErrorFileUrl.ShouldBeNull();
        var job = (await _imports.GetAsync("job", 1, CancellationToken.None))!;
        job.Status.ShouldBe("Failed");
        job.EndDate.ShouldNotBeNull();
        job.Result!["TotalResources"]!.GetValue<int>().ShouldBe(2);
        job.Result["TotalErrors"]!.GetValue<int>().ShouldBe(1);
        job.Result["ErrorFileUrl"].ShouldBeNull();
        job.ErrorMessage.ShouldContain("blob storage unavailable");
        if (originalFailure != null)
        {
            job.ErrorMessage.ShouldContain(originalFailure);
        }
    }

    private IBackgroundJobRepository<ImportJobDefinition> ForwardImports()
    {
        var repository = Substitute.For<IBackgroundJobRepository<ImportJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => _imports.GetAsync("job", 1, CancellationToken.None));
        repository.UpdateAsync(Arg.Any<BackgroundJob<ImportJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(call => _imports.UpdateAsync(call.Arg<BackgroundJob<ImportJobDefinition>>(), 1, CancellationToken.None));
        return repository;
    }

    private ImportCompletion CreateImportCompletion(IBackgroundJobRepository<ImportJobDefinition> repository, IBlobStorageClient? storage = null)
    {
        using var services = new ServiceCollection()
            .AddSingleton(repository)
            .AddSingleton<IBlobStorageClient>(storage ?? _blobs)
            .AddSingleton<ILogger<ImportCompletion>>(NullLogger<ImportCompletion>.Instance)
            .BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<ImportCompletion>(services);
    }

    private async Task<T> RunAsync<T>(TaskActivity activity, object input) =>
        JsonSerializer.Deserialize<T>(await activity.RunAsync(_context, JsonSerializer.Serialize(new[] { input })))!;

    private async Task<string> ReadBlobAsync(string path)
    {
        await using var stream = await _blobs.ReadBlobAsync(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static UpdateProgressInput Progress() => new()
    {
        JobId = "job", TenantId = 1, ProcessedResources = 3, ProcessedFiles = 1, TotalFiles = 2
    };

    private static CompleteJobInput Completion(string error, int total) => new()
    {
        JobId = "job", TenantId = 1, TotalResources = total, TotalErrors = 1,
        ErrorLogEntries = [new ImportErrorLogEntry { ResourceType = "Patient", ResourceId = "p1", ErrorCode = "InvalidResourceType",
            ErrorMessage = error, ResourceJson = "{}" }]
    };

    public Task DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
