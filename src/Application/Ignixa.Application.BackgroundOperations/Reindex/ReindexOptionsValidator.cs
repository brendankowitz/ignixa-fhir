using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexOptionsValidator : IValidateOptions<ReindexOptions>
{
    public ValidateOptionsResult Validate(string? name, ReindexOptions options) => Validate(options);

    public static ValidateOptionsResult Validate(ReindexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidateRange(
            options.DefaultMaximumNumberOfResourcesPerQuery,
            ReindexJobParameters.MaxResourcesPerBatch,
            nameof(options.DefaultMaximumNumberOfResourcesPerQuery),
            failures);
        ValidateRange(
            options.DefaultMaximumNumberOfResourcesPerWrite,
            ReindexJobParameters.MaxResourcesPerBatch,
            nameof(options.DefaultMaximumNumberOfResourcesPerWrite),
            failures);
        ValidateRange(
            options.DefaultMaximumConcurrency, ReindexJobParameters.MaxConcurrency, nameof(options.DefaultMaximumConcurrency), failures);

        ValidatePositive(options.OrphanGrace, nameof(options.OrphanGrace), failures);
        ValidatePositive(options.StaleJobTimeout, nameof(options.StaleJobTimeout), failures);
        ValidatePositive(options.DrainWarningAfter, nameof(options.DrainWarningAfter), failures);
        ValidateRange(options.ContinueAsNewThreshold, int.MaxValue, nameof(options.ContinueAsNewThreshold), failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(int value, int maximum, string name, ICollection<string> failures)
    {
        if (value < 1 || value > maximum)
        {
            failures.Add($"{ReindexOptions.SectionName}:{name} must be between 1 and {maximum}.");
        }
    }

    private static void ValidatePositive(
        TimeSpan value,
        string name,
        ICollection<string> failures)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"{ReindexOptions.SectionName}:{name} must be greater than zero.");
        }
    }
}
