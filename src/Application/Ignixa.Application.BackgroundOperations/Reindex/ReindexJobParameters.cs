namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexJobParameters(
    int MaximumNumberOfResourcesPerQuery,
    int MaximumNumberOfResourcesPerWrite,
    int MaximumConcurrency,
    int QueryDelayIntervalInMilliseconds)
{
    public static ReindexJobParameters Create(
        int maximumNumberOfResourcesPerQuery = 10_000,
        int maximumNumberOfResourcesPerWrite = 1_000,
        int maximumConcurrency = 4,
        int queryDelayIntervalInMilliseconds = 0)
    {
        ValidateRange(
            maximumNumberOfResourcesPerQuery,
            1,
            10_000,
            nameof(maximumNumberOfResourcesPerQuery));
        ValidateRange(
            maximumNumberOfResourcesPerWrite,
            1,
            10_000,
            nameof(maximumNumberOfResourcesPerWrite));
        ValidateRange(maximumConcurrency, 1, 16, nameof(maximumConcurrency));
        ValidateRange(
            queryDelayIntervalInMilliseconds,
            0,
            60_000,
            nameof(queryDelayIntervalInMilliseconds));

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
            throw new ReindexValidationException(
                $"{name} must be between {minimum} and {maximum}.");
        }
    }
}
