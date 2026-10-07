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
        if (options.TransitionGrace <= options.MaxStaleness)
        {
            failures.Add(
                $"{ConformanceTransitionOptions.SectionName}:TransitionGrace must be greater than " +
                $"{ConformanceTransitionOptions.SectionName}:MaxStaleness.");
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
