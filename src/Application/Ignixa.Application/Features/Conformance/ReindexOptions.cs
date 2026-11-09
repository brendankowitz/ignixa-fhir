namespace Ignixa.Application.Features.Conformance;

public sealed class ReindexOptions
{
    public const string SectionName = "Reindex";

    public bool Enabled { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public TimeSpan BarrierDelay { get; set; }
    public int DefaultMaximumNumberOfResourcesPerQuery { get; set; } = 10_000;
    /// <summary>
    /// Gets or sets the number of resources in each reindex index-write batch. The conservative default
    /// avoids lock escalation while the index-only procedure updates multiple search-index tables.
    /// </summary>
    public int DefaultMaximumNumberOfResourcesPerWrite { get; set; } = 100;
    public int DefaultMaximumConcurrency { get; set; } = 4;
    public TimeSpan OrphanGrace { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan StaleJobTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan DrainWarningAfter { get; set; } = TimeSpan.FromMinutes(5);
    public int ContinueAsNewThreshold { get; set; } = 2_000;
}
