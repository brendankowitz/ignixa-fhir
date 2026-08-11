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
/// imperative splitting causes) never carries this marker. Verified directly against real DacFx
/// DeployReport XML captured from this project's own schema. This replaces an earlier, narrower
/// design (a hand-maintained allow-list of known-benign object type/name patterns, "Categories B
/// through F") that needed a new entry every time a migration touched a not-yet-seen table --
/// the DataIssue-alert signal is general and needs no future entries. An unrecognized report
/// shape (wrong root element/namespace, or a missing Operations element) is treated as a
/// classification failure and throws, rather than defaulting to "safe" -- see <see cref="IsAutoSafe"/>.
/// </summary>
public static class DeployReportClassifier
{
    private static readonly XNamespace ReportNamespace = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    private static readonly string[] NeverDestructiveOperations = ["Create", "Refresh"];

    public static bool IsAutoSafe(string deployReportXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(deployReportXml);

        var document = XDocument.Parse(deployReportXml);
        var root = document.Root;
        if (root is null || root.Name != ReportNamespace + "DeploymentReport")
        {
            // Fail closed, not open: an unrecognized root element/namespace means we cannot
            // prove the report is safe (e.g. a future DacFx/SqlPackage version could change the
            // DeployReport schema). Silently treating "I don't understand this shape" the same
            // as "no operations" would defeat the entire purpose of this safety gate.
            throw new InvalidOperationException(
                $"Unrecognized DeployReport shape: expected root element '{{{ReportNamespace}}}DeploymentReport', " +
                $"got '{root?.Name.ToString() ?? "<none>"}'. Refusing to classify as auto-safe.");
        }

        var operationsElement = root.Element(ReportNamespace + "Operations");
        if (operationsElement is null)
        {
            // Distinguish "Operations element present but empty" (a real, valid "no changes"
            // signal -- an empty <Operations /> element is genuinely safe) from "Operations
            // element missing entirely" (a structural/parsing failure that must not be silently
            // treated the same way).
            throw new InvalidOperationException(
                $"Unrecognized DeployReport shape: missing required '{{{ReportNamespace}}}Operations' element. " +
                "Refusing to classify as auto-safe.");
        }

        var operations = operationsElement.Elements(ReportNamespace + "Operation");

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
