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

        // Each default is checked on its own so every out-of-range key is reported, not just the first.
        ValidateJobParameter(
            () => ReindexJobParameters.Create(
                maximumNumberOfResourcesPerQuery: options.DefaultMaximumNumberOfResourcesPerQuery),
            nameof(options.DefaultMaximumNumberOfResourcesPerQuery),
            failures);
        ValidateJobParameter(
            () => ReindexJobParameters.Create(
                maximumNumberOfResourcesPerWrite: options.DefaultMaximumNumberOfResourcesPerWrite),
            nameof(options.DefaultMaximumNumberOfResourcesPerWrite),
            failures);
        ValidateJobParameter(
            () => ReindexJobParameters.Create(maximumConcurrency: options.DefaultMaximumConcurrency),
            nameof(options.DefaultMaximumConcurrency),
            failures);

        ValidatePositive(options.OrphanGrace, nameof(options.OrphanGrace), failures);
        ValidatePositive(options.StaleJobTimeout, nameof(options.StaleJobTimeout), failures);
        ValidatePositive(options.DrainWarningAfter, nameof(options.DrainWarningAfter), failures);
        if (options.ContinueAsNewThreshold < 1)
        {
            failures.Add(
                $"{ReindexOptions.SectionName}:{nameof(options.ContinueAsNewThreshold)} must be between 1 and {int.MaxValue}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateJobParameter(
        Func<ReindexJobParameters> create,
        string optionName,
        ICollection<string> failures)
    {
        try
        {
            create();
        }
        catch (ReindexValidationException exception)
        {
            failures.Add(
                $"{ReindexOptions.SectionName}:{optionName} must be between {exception.Minimum} and {exception.Maximum}.");
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
