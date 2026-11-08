namespace Ignixa.Domain.Constants;

public static class SqlExecutionPolicyDefaults
{
    public const int CommandTimeoutSeconds = 30;
    public const int MaxRetryAttempts = 3;
    public const int RetryBaseDelayMilliseconds = 200;

    public static readonly TimeSpan MaximumExecutionBudget =
        TimeSpan.FromSeconds(CommandTimeoutSeconds * (MaxRetryAttempts + 1)) +
        TimeSpan.FromMilliseconds(
            RetryBaseDelayMilliseconds * ((1 << MaxRetryAttempts) - 1));
}
