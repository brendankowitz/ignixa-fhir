using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.BackgroundOperations.Import.Orchestrations;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class ImportOrchestrationSchedulingTests
{
    [Fact]
    public async Task GivenMoreFilesThanConcurrencyLimit_WhenImporting_ThenProgressIsRecordedBeforeNextFilesStart()
    {
        var context = Substitute.For<OrchestrationContext>();
        context.CurrentUtcDateTime.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        context.ScheduleTask<ValidateFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new ValidateFileOutput { IsValid = true });
        var files = new List<TaskCompletionSource<StreamingImportFileOutput>>();
        var thirdFileScheduled = new TaskCompletionSource();
        var progress = new List<UpdateProgressInput>();
        context.ScheduleTask<StreamingImportFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(_ =>
            {
                var completion = new TaskCompletionSource<StreamingImportFileOutput>();
                files.Add(completion);
                if (files.Count == 3)
                {
                    thirdFileScheduled.SetResult();
                }
                return completion.Task;
            });
        context.ScheduleTask<bool>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(call =>
            {
                progress.Add((UpdateProgressInput)call.Arg<object[]>()[0]);
                return true;
            });
        context.ScheduleTask<CompleteJobOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new CompleteJobOutput { Result = new ImportJobResult { TotalResources = 6, TotalErrors = 0 } });
        var input = CreateInput(bounded: true);

        var execution = new ImportOrchestration().RunTask(context, input);

        files.Count.ShouldBe(2);
        files[0].SetResult(Output("one.ndjson", 1));
        files[1].SetResult(Output("two.ndjson", 2));
        await thirdFileScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        progress.Last().ProcessedFiles.ShouldBe(2);
        progress.Last().ProcessedResources.ShouldBe(3);
        files[2].SetResult(Output("three.ndjson", 3));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        result.Status.ShouldBe("Completed");
        result.TotalResources.ShouldBe(6);
        progress.Last().ProcessedFiles.ShouldBe(3);
        progress.Last().ProcessedResources.ShouldBe(6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void GivenConfiguredFileLimit_WhenSchedulingImport_ThenOnlyThatManyFilesStart(int limit)
    {
        var context = Substitute.For<OrchestrationContext>();
        context.CurrentUtcDateTime.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        context.ScheduleTask<ValidateFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new ValidateFileOutput { IsValid = true });
        var started = 0;
        context.ScheduleTask<StreamingImportFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(_ =>
        {
            started++;
            return new TaskCompletionSource<StreamingImportFileOutput>().Task;
        });
        var json = JsonSerializer.SerializeToNode(CreateInput(bounded: true))!;
        json["MaxConcurrentFiles"] = limit;

        var execution = new ImportOrchestration().RunTask(context, json.Deserialize<ImportOrchestrationInput>()!);

        started.ShouldBe(limit);
        execution.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenRejectedProgressCheckpoint_WhenImporting_ThenNextFileGroupDoesNotStart()
    {
        var context = Substitute.For<OrchestrationContext>();
        context.CurrentUtcDateTime.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        context.ScheduleTask<ValidateFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new ValidateFileOutput { IsValid = true });
        var started = 0;
        context.ScheduleTask<StreamingImportFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(call =>
        {
            started++;
            return Output(((StreamingImportFileInput)call.Arg<object[]>()[0]).FileUrl, 1);
        });
        context.ScheduleTask<bool>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(false);
        CompleteJobInput? completion = null;
        context.ScheduleTask<CompleteJobOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(call =>
        {
            completion = (CompleteJobInput)call.Arg<object[]>()[0];
            return new CompleteJobOutput { Status = completion.ErrorMessage == null ? "Completed" : "Failed",
                ErrorMessage = completion.ErrorMessage, Result = new ImportJobResult { TotalResources = completion.TotalResources, TotalErrors = completion.TotalErrors } };
        });

        var result = await new ImportOrchestration().RunTask(context, CreateInput(bounded: true));

        started.ShouldBe(2);
        result.Status.ShouldBe("Failed");
        completion!.ErrorMessage.ShouldContain("progress");
        completion.TotalResources.ShouldBe(2);
    }

    [Fact]
    public async Task GivenFatalFileFailure_WhenFinalizingImport_ThenCountsAreExplicitlyIncomplete()
    {
        var context = Substitute.For<OrchestrationContext>();
        context.CurrentUtcDateTime.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        context.ScheduleTask<ValidateFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new ValidateFileOutput { IsValid = true });
        context.ScheduleTask<StreamingImportFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(_ => Task.FromException<StreamingImportFileOutput>(new IOException("commit response unavailable")));
        CompleteJobInput? completion = null;
        context.ScheduleTask<CompleteJobOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(call =>
        {
            completion = (CompleteJobInput)call.Arg<object[]>()[0];
            return new CompleteJobOutput { Status = "Failed", ErrorMessage = completion.ErrorMessage };
        });

        var result = await new ImportOrchestration().RunTask(context, CreateInput(bounded: true));

        result.Status.ShouldBe("Failed");
        completion!.TotalErrors.ShouldBe(0);
        completion.ErrorMessage.ShouldContain("commit response unavailable");
        var json = JsonSerializer.SerializeToNode(completion)!;
        json["CountsAreComplete"].ShouldNotBeNull();
        json["CountsAreComplete"]!.GetValue<bool>().ShouldBeFalse();
        json["ResourcesWithUnknownOutcome"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenFinalMetadataFailure_WhenCompletingOrchestration_ThenFinalizationIsNotRecursivelyReentered()
    {
        var context = Substitute.For<OrchestrationContext>();
        context.CurrentUtcDateTime.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        context.ScheduleTask<ValidateFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new ValidateFileOutput { IsValid = true });
        context.ScheduleTask<StreamingImportFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(call => Output(((StreamingImportFileInput)call.Arg<object[]>()[0]).FileUrl, 1));
        context.ScheduleTask<bool>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(true);
        var completions = 0;
        context.ScheduleTask<CompleteJobOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(_ =>
        {
            completions++;
            return Task.FromException<CompleteJobOutput>(new IOException("metadata unavailable"));
        });

        var failure = await Should.ThrowAsync<IOException>(() => new ImportOrchestration().RunTask(context, CreateInput(bounded: true)));

        failure.Message.ShouldContain("metadata unavailable");
        completions.ShouldBe(1);
    }

    private static ImportOrchestrationInput CreateInput(bool bounded)
    {
        var input = new ImportOrchestrationInput
        {
            JobId = "job", TenantId = 1, Mode = "IncrementalLoad", BatchSize = 100, ChannelCapacity = 100,
            InputFiles =
            [
                new InputFileInfo { Type = "Patient", Url = "one.ndjson" },
                new InputFileInfo { Type = "Patient", Url = "two.ndjson" },
                new InputFileInfo { Type = "Patient", Url = "three.ndjson" }
            ]
        };
        var json = JsonSerializer.SerializeToNode(input)!;
        if (bounded)
        {
            json["BoundedFileScheduling"] = true;
        }
        else
        {
            json.AsObject().Remove("BoundedFileScheduling");
        }
        return json.Deserialize<ImportOrchestrationInput>()!;
    }

    private static StreamingImportFileOutput Output(string file, int count) => new()
    {
        FileUrl = file, ResourceType = "Patient", SuccessCount = count, ErrorCount = 0, Duration = TimeSpan.Zero, Errors = []
    };
}
