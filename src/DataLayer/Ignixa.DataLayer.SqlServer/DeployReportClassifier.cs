using System.Xml.Linq;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Classifies a SqlPackage/DacFx DeployReport as safe to auto-apply unattended, or not.
/// Create and Refresh operations are never destructive by construction (Create fails loudly
/// at deploy time rather than silently corrupting; Refresh only recompiles a procedure's
/// schema binding). For every other operation, an Item is unsafe if and only if SqlPackage's
/// own comparison engine flagged it with a child &lt;Issue&gt; element -- this is the same signal
/// DacFx uses internally to raise a DataIssue alert (e.g. "this column is being dropped, data
/// loss could occur"). A purely additive change (a new nullable column, a canonicalization-only
/// default/check-constraint rewrite, the partition-rebuild cascade Script.PostDeployment.sql's
/// imperative splitting causes) never carries this marker. Verified directly against this
/// project's real DeployReport XML -- see docs/superpowers/plans/2026-07-19-ignixa-datalayer-sqlserver-phase-c.md
/// Task 9 for the captured example and the reasoning. This replaces an earlier, narrower design
/// (a hand-maintained allow-list of known-benign object type/name patterns, "Categories B
/// through F") that needed a new entry every time a migration touched a not-yet-seen table --
/// the DataIssue-alert signal is general and needs no future entries.
/// </summary>
public static class DeployReportClassifier
{
    private static readonly XNamespace ReportNamespace = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    private static readonly string[] NeverDestructiveOperations = ["Create", "Refresh"];

    public static bool IsAutoSafe(string deployReportXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(deployReportXml);

        var document = XDocument.Parse(deployReportXml);
        var operations = document.Root?.Element(ReportNamespace + "Operations")?.Elements(ReportNamespace + "Operation")
            ?? [];

        foreach (var operation in operations)
        {
            var operationName = operation.Attribute("Name")?.Value ?? string.Empty;
            if (NeverDestructiveOperations.Contains(operationName))
            {
                continue;
            }

            foreach (var item in operation.Elements(ReportNamespace + "Item"))
            {
                if (item.Element(ReportNamespace + "Issue") is not null)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
