using Ignixa.Domain.Models;

namespace Ignixa.Api.Tests.Services;

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
}
