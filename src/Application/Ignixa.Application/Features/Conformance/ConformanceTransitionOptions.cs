namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceTransitionOptions
{
    public const string SectionName = "Conformance";

    public int SyncIntervalSeconds { get; set; } = 30;
    public TimeSpan MaxStaleness { get; set; }
    public TimeSpan TransitionGrace { get; set; }
}
