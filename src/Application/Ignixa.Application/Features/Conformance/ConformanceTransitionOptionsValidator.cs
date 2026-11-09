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

        if (options.ClockSkewAllowance < TimeSpan.Zero)
        {
            failures.Add($"{ConformanceTransitionOptions.SectionName}:ClockSkewAllowance must not be negative.");
        }

        if (options.TransitionSafetyMargin <= options.ClockSkewAllowance)
        {
            failures.Add(
                $"{ConformanceTransitionOptions.SectionName}:TransitionSafetyMargin must be greater than " +
                $"{ConformanceTransitionOptions.SectionName}:ClockSkewAllowance.");
        }

        // With a positive MaxStaleness these two rules also make TransitionGrace and BarrierDelay positive.
        // Ordering the comparison first keeps the subtraction from overflowing.
        if (options.MaxStaleness > TimeSpan.Zero &&
            (options.TransitionGrace < options.MaxStaleness ||
             options.TransitionGrace - options.MaxStaleness < options.TransitionSafetyMargin))
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
