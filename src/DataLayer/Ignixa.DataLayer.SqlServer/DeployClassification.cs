namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The three possible verdicts <see cref="DeployReportClassifier"/> can reach about a
/// SqlPackage/DacFx DeployReport.
/// </summary>
public enum DeployClassification
{
    /// <summary>
    /// The report could not be understood well enough to reach a verdict (unrecognized element
    /// shape, an alert kind this classifier cannot interpret, or a data-loss alert that couldn't be
    /// reconciled against the inline markers). Treated as not-auto-safe: never applied unattended,
    /// but -- unlike an exception -- an operator can still review it and decide, which is exactly
    /// what Ignixa.SchemaUpgrade.Cli exists to do.
    /// <para>
    /// Deliberately the ZERO value. <c>default(DeployClassification)</c> is what a zero-initialised
    /// field, an uninitialised struct member, or a value read back from a wire/DB default lands on;
    /// having that land on "safe to deploy unattended" would be a fail-open reachable without any
    /// report being classified at all.
    /// </para>
    /// </summary>
    Unclassifiable = 0,

    /// <summary>A destructive change was found; the diff needs operator review before it is applied.</summary>
    Unsafe = 1,

    /// <summary>No destructive change was found; the diff may be applied unattended.</summary>
    AutoSafe = 2,
}
