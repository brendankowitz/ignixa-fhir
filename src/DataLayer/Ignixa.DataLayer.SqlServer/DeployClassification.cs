namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The three possible verdicts <see cref="DeployReportClassifier"/> can reach about a
/// SqlPackage/DacFx DeployReport.
/// </summary>
public enum DeployClassification
{
    /// <summary>No destructive change was found; the diff may be applied unattended.</summary>
    AutoSafe,

    /// <summary>A destructive change was found; the diff needs operator review before it is applied.</summary>
    Unsafe,

    /// <summary>
    /// The report could not be understood well enough to reach a verdict (unrecognized element
    /// shape, or a data-loss alert that couldn't be reconciled against the inline markers). Treated
    /// as not-auto-safe: never applied unattended, but -- unlike an exception -- an operator can
    /// still review it and decide, which is exactly what Ignixa.SchemaUpgrade.Cli exists to do.
    /// </summary>
    Unclassifiable,
}
