namespace Ignixa.Application.Features.Conformance;

public sealed class ReindexOptions
{
    public const string SectionName = "Reindex";

    public bool Enabled { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public TimeSpan BarrierDelay { get; set; }
}
