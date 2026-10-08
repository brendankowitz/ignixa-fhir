namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceTransitionOptions
{
    public const string SectionName = "Conformance";
    public static readonly TimeSpan DefaultTransitionSafetyMargin = TimeSpan.FromSeconds(30);

    public int SyncIntervalSeconds { get; set; } = 30;
    public TimeSpan MaxStaleness { get; set; }
    public TimeSpan TransitionGrace { get; set; }
    public TimeSpan TransitionSafetyMargin { get; set; } = DefaultTransitionSafetyMargin;
}
