using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using DurableTask.Core.Serializing;
using Ignixa.Application.BackgroundOperations.Terminology.Activities;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.Application.BackgroundOperations.Terminology.Orchestrations;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class TerminologyCommittedHistoryTests
{
    [Fact]
    public void GivenACompleteLegacySchedulingTurn_WhenReplayedWithActivityCompletions_ThenItCompletesWithoutNondeterminism()
    {
        long[] ids = [7, 3, 2, 9, 1, 8];
        var input = new TerminologyImportOrchestrationInput(1, "legacy.package", "1", ids);
        var serialized = JsonDataConverter.Default.Serialize(input);
        serialized.ShouldNotContain("DependencyPlan");
        JsonDataConverter.Default.Serialize(new ImportTerminologyResourceInput(1, 7)).ShouldNotContain("DependencyFailure");
        var firstTurn = Start(serialized);
        var legacyActions = Execute(firstTurn, new LegacySchedulingTurn()).Actions
            .OfType<ScheduleTaskOrchestratorAction>().ToArray();
        legacyActions.Length.ShouldBe(6);

        // Persist the entire original scheduling turn, not an invented partial prefix of its actions.
        var committed = Start(serialized);
        committed.AddRange(legacyActions.Select(action =>
            (HistoryEvent)new TaskScheduledEvent(action.Id, action.Name, action.Version, action.Input)));
        committed.Add(new OrchestratorCompletedEvent(-1));
        var state = new OrchestrationRuntimeState(committed);
        state.AddEvent(new OrchestratorStartedEvent(-1) { Timestamp = DateTime.UnixEpoch.AddSeconds(1) });
        foreach (var action in legacyActions.Reverse())
        {
            var activityInput = JsonDataConverter.Default.Deserialize<ImportTerminologyResourceInput[]>(action.Input).Single();
            var output = new ImportTerminologyResourceOutput(activityInput.PackageResourceId,
                $"http://example.org/{activityInput.PackageResourceId}", "CodeSystem", true, 3, null);
            state.AddEvent(new TaskCompletedEvent(-1, action.Id, JsonDataConverter.Default.Serialize(output)));
        }

        var result = new TaskOrchestrationExecutor(state, new TerminologyImportOrchestration(),
            BehaviorOnContinueAsNew.Ignore, ErrorPropagationMode.UseFailureDetails).Execute();

        var completion = result.Actions.OfType<OrchestrationCompleteOrchestratorAction>().Single();
        completion.OrchestrationStatus.ShouldBe(OrchestrationStatus.Completed);
        var actual = JsonDataConverter.Default.Deserialize<TerminologyImportOrchestrationOutput>(completion.Result);
        actual.Success.ShouldBeTrue(actual.ErrorMessage);
        actual.Results.Select(resource => resource.PackageResourceId).ShouldBe(ids);
        actual.TotalConceptsImported.ShouldBe(18);
        result.Actions.OfType<ScheduleTaskOrchestratorAction>().ShouldBeEmpty();
    }

    private static List<HistoryEvent> Start(string input) =>
    [
        new OrchestratorStartedEvent(-1) { Timestamp = DateTime.UnixEpoch },
        new ExecutionStartedEvent(-1, input)
        {
            Name = typeof(TerminologyImportOrchestration).FullName,
            Version = "",
            OrchestrationInstance = new OrchestrationInstance { InstanceId = "committed-terminology", ExecutionId = "legacy" },
            Timestamp = DateTime.UnixEpoch,
        },
    ];

    private static OrchestratorExecutionResult Execute(List<HistoryEvent> history, TaskOrchestration orchestration)
        => new TaskOrchestrationExecutor(new OrchestrationRuntimeState(history), orchestration,
            BehaviorOnContinueAsNew.Ignore, ErrorPropagationMode.UseFailureDetails).Execute();

    private sealed class LegacySchedulingTurn : TaskOrchestration<TerminologyImportOrchestrationOutput, TerminologyImportOrchestrationInput>
    {
        public override async Task<TerminologyImportOrchestrationOutput> RunTask(
            OrchestrationContext context, TerminologyImportOrchestrationInput input)
        {
            var tasks = input.PackageResourceIds.Select(id => context.ScheduleTask<ImportTerminologyResourceOutput>(
                typeof(ImportTerminologyResourceActivity), new ImportTerminologyResourceInput(input.TenantId, id))).ToList();
            var results = new List<TerminologyImportResourceResult>();
            foreach (var batch in tasks.Chunk(5))
            {
                results.AddRange((await Task.WhenAll(batch)).Select(result => new TerminologyImportResourceResult(
                    result.PackageResourceId, result.Canonical, result.ResourceType, result.Success, result.ConceptCount, result.ErrorMessage)));
            }
            return new TerminologyImportOrchestrationOutput(true, results.Count, results.Sum(r => r.ConceptCount),
                results.Count, 0, 0, results, null, null);
        }
    }
}
