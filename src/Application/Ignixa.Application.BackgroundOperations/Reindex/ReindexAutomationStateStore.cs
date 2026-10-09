using System.Text.Json.Nodes;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexAutomationStateStore(
    IBackgroundJobRepository<ReindexJobDefinition> repository)
{
    public const string StateJobId = "reindex-automation-state";
    private const int GlobalTenantId = 1;
    private const string RequestedGenerationName = "requestedGeneration";

    public async Task<long> GetRequestedGenerationAsync(CancellationToken cancellationToken)
    {
        var state = await repository.GetAsync(StateJobId, GlobalTenantId, cancellationToken);
        return state?.Progress?[RequestedGenerationName]?.GetValue<long>() ?? 0;
    }

    public async Task<long> IncrementRequestedGenerationAsync(CancellationToken cancellationToken)
    {
        var state = await repository.GetAsync(StateJobId, GlobalTenantId, cancellationToken);
        if (state is null)
        {
            await repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
            {
                JobId = StateJobId,
                JobType = (int)BackgroundJobType.ReindexAutomation,
                Status = "Active",
                Definition = CreateAutomationDefinition(),
                Progress = new JsonObject { [RequestedGenerationName] = 1 },
                CreateDate = DateTimeOffset.UtcNow,
                HeartbeatDate = DateTimeOffset.UtcNow
            }, cancellationToken);
            return 1;
        }

        var generation = (state.Progress?[RequestedGenerationName]?.GetValue<long>() ?? 0) + 1;
        state.Progress ??= new JsonObject();
        state.Progress[RequestedGenerationName] = generation;
        await repository.UpdateAsync(state, GlobalTenantId, cancellationToken);
        return generation;
    }

    private static ReindexJobDefinition CreateAutomationDefinition() => new()
    {
        TargetEventId = 0,
        TenantIds = [GlobalTenantId],
        ResourceTypes = [],
        SearchParameters = [],
        MaximumNumberOfResourcesPerQuery = 10_000,
        MaximumNumberOfResourcesPerWrite = 1_000,
        MaximumConcurrency = 4,
        QueryDelayIntervalInMilliseconds = 0,
        Trigger = "Automation",
        ConsumedGeneration = 0
    };
}
