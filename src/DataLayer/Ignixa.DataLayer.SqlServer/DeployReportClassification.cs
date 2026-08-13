namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The verdict <see cref="DeployReportClassifier.Classify"/> reached about a DeployReport, together
/// with the specific findings behind it. <see cref="Reasons"/> is what makes an
/// <see cref="DeployClassification.Unsafe"/> or <see cref="DeployClassification.Unclassifiable"/>
/// result actionable: it names the flagged objects (or the structural problem) so callers can put
/// them in an exception message or in front of an operator, instead of telling them to re-read raw
/// XML.
/// </summary>
/// <param name="Outcome">The verdict.</param>
/// <param name="Reasons">
/// Human-readable findings supporting <paramref name="Outcome"/>. Empty when
/// <paramref name="Outcome"/> is <see cref="DeployClassification.AutoSafe"/>.
/// </param>
public sealed record DeployReportClassification(
    DeployClassification Outcome,
    IReadOnlyList<string> Reasons)
{
    /// <summary>True only when the diff is safe to apply unattended.</summary>
    public bool IsAutoSafe => Outcome == DeployClassification.AutoSafe;

    /// <summary>Findings joined for embedding in an exception message or console output.</summary>
    public string ReasonSummary => Reasons.Count == 0 ? "(none)" : string.Join("; ", Reasons);
}
