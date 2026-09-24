using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.BackgroundOperations.Import.Orchestrations;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class LegacyImportSchedulingCompatibilityTests
{
    [Fact]
    public void GivenLegacyPersistedInput_WhenResumingImport_ThenRecordedSchedulingOrderIsPreserved()
    {
        var context = Context();
        var scheduledFiles = 0;
        context.ScheduleTask<StreamingImportFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(_ =>
        {
            scheduledFiles++;
            return new TaskCompletionSource<StreamingImportFileOutput>().Task;
        });

        var execution = new ImportOrchestration().RunTask(context, Input());

        scheduledFiles.ShouldBe(3);
        execution.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenLegacyEarlyWorkerFailure_WhenLaterWorkIsStillPending_ThenFailureDoesNotWaitForThatLaterWork()
    {
        var context = Context();
        var files = new List<TaskCompletionSource<StreamingImportFileOutput>>();
        context.ScheduleTask<StreamingImportFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(_ =>
        {
            var completion = new TaskCompletionSource<StreamingImportFileOutput>();
            files.Add(completion);
            return completion.Task;
        });
        context.ScheduleTask<CompleteJobOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(new CompleteJobOutput());

        var execution = new ImportOrchestration().RunTask(context, Input());
        files.Count.ShouldBe(3);
        files[0].SetException(new IOException("first pair failed"));
        files[1].SetResult(Output("two.ndjson"));

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        result.Status.ShouldBe("Failed");
        files[2].Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenLegacyFalseCheckpoint_WhenResumingImport_ThenRecordedProgressAndCompletionOrderIsPreserved()
    {
        var context = Context();
        var actions = new List<string>();
        context.ScheduleTask<StreamingImportFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(call =>
        {
            var file = ((StreamingImportFileInput)call.Arg<object[]>()[0]).FileUrl;
            actions.Add(file);
            return Output(file);
        });
        context.ScheduleTask<bool>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(call =>
        {
            actions.Add($"progress-{((UpdateProgressInput)call.Arg<object[]>()[0]).ProcessedFiles}");
            return false;
        });
        context.ScheduleTask<CompleteJobOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(_ =>
        {
            actions.Add("complete");
            return new CompleteJobOutput { Result = new ImportJobResult { TotalResources = 3, TotalErrors = 0 } };
        });

        await new ImportOrchestration().RunTask(context, Input());

        actions.ShouldBe(["one.ndjson", "two.ndjson", "three.ndjson", "progress-1", "progress-2", "progress-3", "complete"]);
    }

    private static OrchestrationContext Context()
    {
        var context = Substitute.For<OrchestrationContext>();
        context.CurrentUtcDateTime.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        context.ScheduleTask<ValidateFileOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new ValidateFileOutput { IsValid = true });
        return context;
    }

    private static ImportOrchestrationInput Input() => JsonSerializer.Deserialize<ImportOrchestrationInput>("""
        {"JobId":"job","TenantId":1,"Mode":"IncrementalLoad","BatchSize":100,"ChannelCapacity":100,
         "InputFiles":[{"Type":"Patient","Url":"one.ndjson"},{"Type":"Patient","Url":"two.ndjson"},{"Type":"Patient","Url":"three.ndjson"}]}
        """)!;

    private static StreamingImportFileOutput Output(string file) => new()
    {
        FileUrl = file, ResourceType = "Patient", SuccessCount = 1, ErrorCount = 0, Duration = TimeSpan.Zero, Errors = []
    };
}
