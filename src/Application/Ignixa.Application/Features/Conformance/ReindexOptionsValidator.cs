using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Conformance;

public sealed class ReindexOptionsValidator : IValidateOptions<ReindexOptions>
{
    public ValidateOptionsResult Validate(string? name, ReindexOptions options) => Validate(options);

    public static ValidateOptionsResult Validate(ReindexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        ValidateRange(
            options.DefaultMaximumNumberOfResourcesPerQuery,
            1,
            10_000,
            nameof(options.DefaultMaximumNumberOfResourcesPerQuery),
            failures);
        ValidateRange(
            options.DefaultMaximumNumberOfResourcesPerWrite,
            1,
            10_000,
            nameof(options.DefaultMaximumNumberOfResourcesPerWrite),
            failures);
        ValidateRange(
            options.DefaultMaximumConcurrency,
            1,
            16,
            nameof(options.DefaultMaximumConcurrency),
            failures);

        ValidatePositive(options.StartDebounce, nameof(options.StartDebounce), allowZero: true, failures);
        ValidatePositive(options.OrphanGrace, nameof(options.OrphanGrace), allowZero: false, failures);
        ValidatePositive(options.StaleJobTimeout, nameof(options.StaleJobTimeout), allowZero: false, failures);
        ValidatePositive(options.DrainWarningAfter, nameof(options.DrainWarningAfter), allowZero: false, failures);
        ValidateRange(
            options.ContinueAsNewThreshold,
            1,
            int.MaxValue,
            nameof(options.ContinueAsNewThreshold),
            failures);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string name,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"{ReindexOptions.SectionName}:{name} must be between {minimum} and {maximum}.");
        }
    }

    private static void ValidatePositive(
        TimeSpan value,
        string name,
        bool allowZero,
        ICollection<string> failures)
    {
        if (allowZero ? value < TimeSpan.Zero : value <= TimeSpan.Zero)
        {
            failures.Add(
                $"{ReindexOptions.SectionName}:{name} must be " +
                (allowZero ? "greater than or equal to zero." : "greater than zero."));
        }
    }
}
