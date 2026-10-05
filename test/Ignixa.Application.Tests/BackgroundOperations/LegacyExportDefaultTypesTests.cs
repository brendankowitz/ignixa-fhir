using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using DurableTask.Core.Serializing;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class LegacyExportDefaultTypesTests
{
    [Fact]
    public void GivenCommittedEmptyTypeInput_WhenReplaying_ThenTheSixLegacyRangeRequestsStillMatchHistory()
    {
        var input = new ExportCoordinatorInput("legacy-export", 1, []);
        var history = new List<HistoryEvent>
        {
            new OrchestratorStartedEvent(-1) { Timestamp = DateTime.UnixEpoch },
            new ExecutionStartedEvent(-1, JsonDataConverter.Default.Serialize(input))
            {
                Name = typeof(ExportOrchestration).FullName, Version = "",
                OrchestrationInstance = new OrchestrationInstance { InstanceId = input.JobId, ExecutionId = "legacy" },
                Timestamp = DateTime.UnixEpoch
            }
        };
        string[] legacyTypes = ["Patient", "Observation", "Condition", "MedicationRequest", "Encounter", "Procedure"];
        for (var id = 0; id < legacyTypes.Length; id++)
        {
            var turn = Execute(history);
            var next = turn.Actions.OfType<ScheduleTaskOrchestratorAction>().Single();
            next.Name.ShouldBe(typeof(GetExportRangesActivity).FullName);
            var nextInput = JsonDataConverter.Default.Deserialize<GetExportRangesInput[]>(next.Input).Single();
            nextInput.ResourceType.ShouldBe(legacyTypes[id]);
            nextInput.NumberOfRanges.ShouldBe(6);
            history.Add(new TaskScheduledEvent(id, typeof(GetExportRangesActivity).FullName, "",
                JsonDataConverter.Default.Serialize(new[] { new GetExportRangesInput(1, legacyTypes[id], 6) })));
            history.Add(new OrchestratorCompletedEvent(-1));
            history.Add(new OrchestratorStartedEvent(-1) { Timestamp = DateTime.UnixEpoch.AddSeconds(id + 1) });
            history.Add(new TaskCompletedEvent(-1, id,
                JsonDataConverter.Default.Serialize(new GetExportRangesOutput(legacyTypes[id], []))));
        }

        var result = Execute(history);

        var completion = result.Actions.OfType<ScheduleTaskOrchestratorAction>().Single();
        completion.Name.ShouldBe(typeof(CompleteJobActivity).FullName);
        var completionInput = JsonDataConverter.Default.Deserialize<CompleteJobInput[]>(completion.Input).Single();
        completionInput.Success.ShouldBeTrue(completionInput.ErrorMessage);
        completionInput.TotalResourcesExported.ShouldBe(0);
        result.Actions.OfType<OrchestrationCompleteOrchestratorAction>().ShouldBeEmpty();
    }

    private static OrchestratorExecutionResult Execute(List<HistoryEvent> history) =>
        new TaskOrchestrationExecutor(new OrchestrationRuntimeState(history), new ExportOrchestration(),
            BehaviorOnContinueAsNew.Ignore, ErrorPropagationMode.UseFailureDetails).Execute();
}
