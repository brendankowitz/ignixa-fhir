using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

internal static class ReindexTestHelper
{
    public static ReindexJobDefinition CreateJobDefinition() => new()
    {
        TargetEventId = 0,
        TenantIds = [1],
        ResourceTypes = [],
        SearchParameters = [],
        MaximumNumberOfResourcesPerQuery = 10_000,
        MaximumNumberOfResourcesPerWrite = 1_000,
        MaximumConcurrency = 4,
        QueryDelayIntervalInMilliseconds = 0,
        Trigger = "Manual",
        ConsumedGeneration = 0
    };

    public static ReindexOrchestrationInput CreateOrchestrationInput(
        string jobId,
        long targetEventId,
        TimeSpan barrierDelay,
        IReadOnlyList<int> tenantIds) =>
        new(
            jobId,
            targetEventId,
            barrierDelay,
            tenantIds,
            ["Patient"],
            [],
            ReindexJobParameters.Create());
}
