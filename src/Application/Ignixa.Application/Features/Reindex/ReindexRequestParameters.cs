namespace Ignixa.Application.Features.Reindex;

public sealed record ReindexRequestParameters(
    int? MaximumNumberOfResourcesPerQuery,
    int? MaximumNumberOfResourcesPerWrite,
    int? MaximumConcurrency,
    int? QueryDelayIntervalInMilliseconds);
