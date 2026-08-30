namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The verdict <see cref="DeployReportClassifier.Classify"/> reached about a DeployReport, together
/// with the specific findings behind it. <see cref="Reasons"/> is what makes an
/// <see cref="DeployClassification.Unsafe"/> or <see cref="DeployClassification.Unclassifiable"/>
/// result actionable: it names the flagged objects (or the structural problem) so callers can put
/// them in an exception message or in front of an operator, instead of telling them to re-read raw
/// XML.
/// </summary>
public sealed record DeployReportClassification
{
    private DeployReportClassification(DeployClassification outcome, IReadOnlyList<string> reasons)
    {
        Outcome = outcome;
        Reasons = reasons;
    }

    /// <summary>The verdict.</summary>
    public DeployClassification Outcome { get; }

    /// <summary>
    /// Human-readable findings supporting <see cref="Outcome"/>. Always empty for
    /// <see cref="DeployClassification.AutoSafe"/>, always non-empty otherwise.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; }

    /// <summary>True only when the diff is safe to apply unattended.</summary>
    public bool IsAutoSafe => Outcome == DeployClassification.AutoSafe;

    /// <summary>Findings joined for embedding in an exception message or console output.</summary>
    public string ReasonSummary => Reasons.Count == 0 ? "(none)" : string.Join("; ", Reasons);

    /// <summary>The diff is safe to apply unattended.</summary>
    public static DeployReportClassification AutoSafe() => new(DeployClassification.AutoSafe, []);

    /// <summary>The diff contains at least one change flagged as a genuine data-loss risk.</summary>
    public static DeployReportClassification Unsafe(IReadOnlyList<string> reasons)
        => new(DeployClassification.Unsafe, RequireReasons(reasons));

    /// <summary>The diff's report shape could not be reliably classified as safe or unsafe.</summary>
    public static DeployReportClassification Unclassifiable(IReadOnlyList<string> reasons)
        => new(DeployClassification.Unclassifiable, RequireReasons(reasons));

    private static IReadOnlyList<string> RequireReasons(IReadOnlyList<string> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Count == 0)
        {
            throw new ArgumentException("At least one reason is required for a non-AutoSafe classification.", nameof(reasons));
        }

        return reasons;
    }
}
