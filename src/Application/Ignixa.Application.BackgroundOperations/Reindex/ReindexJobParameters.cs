namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// The tuning parameters of one reindex job. <see cref="Create"/> is the one place their ranges are
/// enforced, for request parameters and for the configured defaults alike.
/// </summary>
public sealed record ReindexJobParameters(
    int MaximumNumberOfResourcesPerQuery,
    int MaximumNumberOfResourcesPerWrite,
    int MaximumConcurrency,
    int QueryDelayIntervalInMilliseconds)
{
    public const int MaxResourcesPerBatch = 10_000;
    public const int MaxConcurrency = 16;
    public const int MaxQueryDelayInMilliseconds = 60_000;

    public static ReindexJobParameters Create(
        int maximumNumberOfResourcesPerQuery = MaxResourcesPerBatch,
        int maximumNumberOfResourcesPerWrite = 100,
        int maximumConcurrency = 4,
        int queryDelayIntervalInMilliseconds = 0)
    {
        ValidateRange(maximumNumberOfResourcesPerQuery, 1, MaxResourcesPerBatch, nameof(maximumNumberOfResourcesPerQuery));
        ValidateRange(maximumNumberOfResourcesPerWrite, 1, MaxResourcesPerBatch, nameof(maximumNumberOfResourcesPerWrite));
        ValidateRange(maximumConcurrency, 1, MaxConcurrency, nameof(maximumConcurrency));
        ValidateRange(queryDelayIntervalInMilliseconds, 0, MaxQueryDelayInMilliseconds, nameof(queryDelayIntervalInMilliseconds));

        return new ReindexJobParameters(
            maximumNumberOfResourcesPerQuery,
            maximumNumberOfResourcesPerWrite,
            maximumConcurrency,
            queryDelayIntervalInMilliseconds);
    }

    private static void ValidateRange(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new ReindexValidationException(name, minimum, maximum);
        }
    }
}
