namespace Ignixa.PackageManagement.Models;

/// <summary>Attempt count includes the first request. All work shares the overall deadline.</summary>
public sealed class PackageAcquisitionRetryPolicy
{
    public PackageAcquisitionRetryPolicy(
        int maxAttempts = 3, TimeSpan? attemptTimeout = null, TimeSpan? totalTimeout = null,
        TimeSpan? initialDelay = null, TimeSpan? maxDelay = null)
    {
        MaxAttempts = maxAttempts;
        AttemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(120);
        TotalTimeout = totalTimeout ?? TimeSpan.FromSeconds(300);
        InitialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        MaxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        ValidateDuration(AttemptTimeout, nameof(attemptTimeout));
        ValidateDuration(TotalTimeout, nameof(totalTimeout));
        ValidateDuration(InitialDelay, nameof(initialDelay));
        ValidateDuration(MaxDelay, nameof(maxDelay));
        if (AttemptTimeout > TotalTimeout || MaxDelay > TotalTimeout || InitialDelay > MaxDelay)
        {
            throw new ArgumentException("Retry durations must fit the maximum delay and overall deadline.");
        }
    }

    public int MaxAttempts { get; }
    public TimeSpan AttemptTimeout { get; }
    public TimeSpan TotalTimeout { get; }
    public TimeSpan InitialDelay { get; }
    public TimeSpan MaxDelay { get; }

    private static void ValidateDuration(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A positive supported timer duration is required.");
        }
    }
}
