using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceTransitionOptionsValidator(IOptions<ReindexOptions> reindexOptions)
    : IValidateOptions<ConformanceTransitionOptions>
{
    public ValidateOptionsResult Validate(string? name, ConformanceTransitionOptions options) =>
        Validate(options, reindexOptions.Value);

    public static ValidateOptionsResult Validate(
        ConformanceTransitionOptions options,
        ReindexOptions reindexOptions)
    {
        var failures = new List<string>();
        if (options.SyncIntervalSeconds <= 0)
        {
            failures.Add($"{ConformanceTransitionOptions.SectionName}:SyncIntervalSeconds must be greater than zero.");
        }

        if (options.MaxStaleness <= TimeSpan.Zero)
        {
            failures.Add($"{ConformanceTransitionOptions.SectionName}:MaxStaleness must be greater than zero.");
        }

        if (options.TransitionGrace <= TimeSpan.Zero)
        {
            failures.Add($"{ConformanceTransitionOptions.SectionName}:TransitionGrace must be greater than zero.");
        }

        if (options.TransitionSafetyMargin < TimeSpan.Zero)
        {
            failures.Add(
                $"{ConformanceTransitionOptions.SectionName}:TransitionSafetyMargin must be greater than or equal to zero.");
        }

        if (reindexOptions.BarrierDelay < TimeSpan.Zero)
        {
            failures.Add($"{ReindexOptions.SectionName}:BarrierDelay must be greater than or equal to zero.");
        }

        if (options.TransitionGrace < options.MaxStaleness + options.TransitionSafetyMargin)
        {
            failures.Add(
                $"{ConformanceTransitionOptions.SectionName}:TransitionGrace must be greater than or equal to " +
                $"{ConformanceTransitionOptions.SectionName}:MaxStaleness plus " +
                $"{ConformanceTransitionOptions.SectionName}:TransitionSafetyMargin.");
        }

        if (reindexOptions.BarrierDelay < options.MaxStaleness)
        {
            failures.Add(
                $"{ReindexOptions.SectionName}:BarrierDelay must be greater than or equal to " +
                $"{ConformanceTransitionOptions.SectionName}:MaxStaleness.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
