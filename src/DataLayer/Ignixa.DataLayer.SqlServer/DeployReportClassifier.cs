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

        var operations = operationsElement.Elements(ReportNamespace + "Operation").ToList();

        // Assert non-emptiness at each level we descend. The root/Operations guards above only
        // validate the envelope; without these, an unrecognized *payload* (e.g. a future DacFx
        // version renaming or re-namespacing Operation/Item) yields an empty sequence, the loop
        // body never runs, and we fall through to "auto-safe" -- reintroducing the exact fail-open
        // this gate exists to prevent, just one level deeper. A genuinely empty <Operations />
        // (no children at all) remains the legitimate "no pending changes" signal and stays safe.
        if (operationsElement.HasElements && operations.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unrecognized DeployReport shape: '{{{ReportNamespace}}}Operations' has child elements but none " +
                $"named '{{{ReportNamespace}}}Operation'. Refusing to classify as auto-safe.");
        }

        foreach (var operation in operations)
        {
            var operationName = operation.Attribute("Name")?.Value ?? string.Empty;
            if (NeverDestructiveOperations.Contains(operationName))
            {
                continue;
            }

            var items = operation.Elements(ReportNamespace + "Item").ToList();
            if (operation.HasElements && items.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Unrecognized DeployReport shape: operation '{operationName}' has child elements but none " +
                    $"named '{{{ReportNamespace}}}Item'. Refusing to classify as auto-safe.");
            }

            foreach (var item in items)
            {
                if (item.Element(ReportNamespace + "Issue") is not null)
                {
                    return false;
                }
            }
        }

        // Cross-check the <Alerts> block against what we found inline. DacFx raises a DataIssue
        // alert and marks the corresponding Item with a child <Issue> that cross-references it;
        // this class's whole premise is that those two signals agree. If a report declares a
        // DataIssue alert but no non-exempt Item carried an Issue child, the premise doesn't hold
        // for this document and we cannot prove it's safe -- fail closed rather than trusting the
        // inline signal we happen to understand.
        var hasDataIssueAlert = root.Element(ReportNamespace + "Alerts")
            ?.Elements(ReportNamespace + "Alert")
            .Any(alert => string.Equals(alert.Attribute("Name")?.Value, "DataIssue", StringComparison.Ordinal))
            ?? false;

        if (hasDataIssueAlert)
        {
            throw new InvalidOperationException(
                "Unrecognized DeployReport shape: the report declares a DataIssue alert, but no non-exempt Item " +
                $"carried a child '{{{ReportNamespace}}}Issue' element cross-referencing it. Refusing to classify " +
                "as auto-safe.");
        }

        return true;
    }
}
