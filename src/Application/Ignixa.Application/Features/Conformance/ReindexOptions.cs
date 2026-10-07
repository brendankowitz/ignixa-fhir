namespace Ignixa.Application.Features.Conformance;

public sealed class ReindexOptions
{
    public const string SectionName = "Reindex";

    public bool Enabled { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public TimeSpan BarrierDelay { get; set; }
    public int DefaultMaximumNumberOfResourcesPerQuery { get; set; } = 10_000;
    public int DefaultMaximumNumberOfResourcesPerWrite { get; set; } = 1_000;
    public int DefaultMaximumConcurrency { get; set; } = 4;
    public TimeSpan StartDebounce { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan StaleJobTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan DrainWarningAfter { get; set; } = TimeSpan.FromMinutes(5);
    public int ContinueAsNewThreshold { get; set; } = 2_000;
    public int RecentTerminalJobsListed { get; set; } = 10;
}
