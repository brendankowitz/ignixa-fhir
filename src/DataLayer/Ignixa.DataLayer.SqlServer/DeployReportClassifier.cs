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
/// the DataIssue-alert signal is general and needs no future entries.
/// <para>
/// Fail-closed on anything it cannot read: an unrecognized element shape, or a DataIssue alert it
/// cannot reconcile against the inline markers, yields <see cref="DeployClassification.Unclassifiable"/>
/// rather than a silent "safe". That verdict is returned as data rather than thrown, so
/// Ignixa.SchemaUpgrade.Cli -- whose entire purpose is letting an operator review and apply what
/// the automatic path refused -- can still print the diff and prompt instead of dying on a stack trace.
/// </para>
/// </summary>
public static class DeployReportClassifier
{
    private static readonly XNamespace ReportNamespace = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    private static readonly string[] NeverDestructiveOperations = ["Create", "Refresh"];

    private static readonly DeployReportClassification AutoSafeResult = DeployReportClassification.AutoSafe();

    /// <summary>
    /// Classifies <paramref name="deployReportXml"/>. Throws only for a caller bug (null/empty
    /// input) or input that is not XML at all (<see cref="System.Xml.XmlException"/>); every
    /// report-<em>shape</em> problem is reported as <see cref="DeployClassification.Unclassifiable"/>
    /// so callers can act on it rather than catch it.
    /// </summary>
    public static DeployReportClassification Classify(string deployReportXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(deployReportXml);

        var document = XDocument.Parse(deployReportXml);
        var root = document.Root;
        if (root is null || root.Name != ReportNamespace + "DeploymentReport")
        {
            return Unclassifiable(
                $"Expected root element '{{{ReportNamespace}}}DeploymentReport', got '{root?.Name.ToString() ?? "<none>"}'.");
        }

        var operationsElement = root.Element(ReportNamespace + "Operations");
        if (operationsElement is null)
        {
            // An <Operations /> element that is present but empty is the legitimate "no pending
            // changes" signal; one missing entirely is a structural problem and must not be
            // treated the same way.
            return Unclassifiable($"Missing required '{{{ReportNamespace}}}Operations' element.");
        }

        // Require EVERY child to be recognized, not merely at least one. A partial rename -- one
        // valid <Operation> alongside an unrecognized sibling carrying the destructive marker --
        // would otherwise slip through, which is the same fail-open class this gate exists to close.
        var unrecognizedOperations = UnrecognizedChildNames(operationsElement, ReportNamespace + "Operation");
        if (unrecognizedOperations.Count > 0)
        {
            return Unclassifiable(
                $"'{{{ReportNamespace}}}Operations' contains unrecognized child element(s): {string.Join(", ", unrecognizedOperations)}.");
        }

        // An <Operations> element carrying content we can't see as elements (e.g. a future DacFx
        // moving operations into text or attributes) is not the same as a genuinely empty one.
        if (!operationsElement.HasElements && !string.IsNullOrWhiteSpace(operationsElement.Value))
        {
            return Unclassifiable(
                $"'{{{ReportNamespace}}}Operations' has no child elements but carries text content, so no operation could be inspected.");
        }

        var flaggedItems = new List<FlaggedItem>();

        foreach (var operation in operationsElement.Elements(ReportNamespace + "Operation"))
        {
            var operationName = operation.Attribute("Name")?.Value ?? string.Empty;

            var unrecognizedItems = UnrecognizedChildNames(operation, ReportNamespace + "Item");
            if (unrecognizedItems.Count > 0)
            {
                return Unclassifiable(
                    $"Operation '{operationName}' contains unrecognized child element(s): {string.Join(", ", unrecognizedItems)}.");
            }

            var items = operation.Elements(ReportNamespace + "Item").ToList();

            // Every operation in a real DacFx report names the objects it affects via Item
            // children (verified against this project's captured report: Drop/Create/UnbindTable/
            // TableRebuild/Refresh all carry at least one). An Operation with none is a shape we
            // cannot inspect -- reject rather than skipping it and reporting "nothing found".
            if (items.Count == 0)
            {
                return Unclassifiable(
                    $"Operation '{operationName}' contains no '{{{ReportNamespace}}}Item' children, so its affected objects could not be inspected.");
            }

            var isNeverDestructive = NeverDestructiveOperations.Contains(operationName);

            foreach (var item in items)
            {
                // Validate Item's children too. Without this the fail-open simply moves one level
                // deeper again: a renamed or re-namespaced <Issue> on a Drop leaves the item
                // looking unflagged, and the whole report classifies as auto-safe. Issue is the
                // only child element real DacFx reports put here.
                var unrecognizedIssues = UnrecognizedChildNames(item, ReportNamespace + "Issue");
                if (unrecognizedIssues.Count > 0)
                {
                    return Unclassifiable(
                        $"Operation '{operationName}' item '{item.Attribute("Value")?.Value ?? "<unnamed>"}' contains " +
                        $"unrecognized child element(s): {string.Join(", ", unrecognizedIssues)}.");
                }

                if (item.Element(ReportNamespace + "Issue") is not { } issue)
                {
                    continue;
                }

                flaggedItems.Add(new FlaggedItem(
                    OperationName: operationName,
                    ItemValue: item.Attribute("Value")?.Value ?? item.Attribute("Type")?.Value ?? "<unnamed>",
                    IssueId: issue.Attribute("Id")?.Value,
                    IsNeverDestructive: isNeverDestructive));
            }
        }

        var destructive = flaggedItems.Where(f => !f.IsNeverDestructive).ToList();
        if (destructive.Count > 0)
        {
            return DeployReportClassification.Unsafe(
                destructive.Select(f => $"{f.OperationName} {f.ItemValue} is flagged by DacFx as a data issue").ToList());
        }

        // Reconcile the <Alerts> block against the inline markers BY ID. DacFx raises a DataIssue
        // alert and marks the corresponding Item with a child <Issue Id="N"/> cross-referencing it;
        // this class's premise is that those two signals agree. An existence-only check ("did I see
        // any Issue anywhere?") is not enough -- an unrelated marker elsewhere in the document
        // would discharge a genuinely unaccounted-for data-loss alert.
        var alertIssueIds = DataIssueAlertIds(root);
        if (alertIssueIds.Count == 0)
        {
            return AutoSafeResult;
        }

        var reasons = new List<string>();
        foreach (var alertId in alertIssueIds)
        {
            var match = flaggedItems.FirstOrDefault(f => string.Equals(f.IssueId, alertId, StringComparison.Ordinal));
            reasons.Add(match is null
                ? $"DataIssue alert Id={alertId} is not cross-referenced by any Item's Issue element, so the " +
                  "data-loss signal this classifier relies on cannot be located"
                // Reaching here means the alert resolved to an item on a Create/Refresh operation:
                // DacFx says this change can lose data, while this classifier's premise says that
                // operation kind never destroys anything. The premise doesn't hold for this
                // document, so defer to a human rather than trusting either half of the signal.
                : $"DataIssue alert Id={alertId} resolves to {match.OperationName} {match.ItemValue}, an operation " +
                  "kind this classifier treats as never destructive -- the alert and the operation kind disagree");
        }

        return DeployReportClassification.Unclassifiable(reasons);
    }

    private static List<string> UnrecognizedChildNames(XElement parent, XName expected)
        => parent.Elements()
            .Where(e => e.Name != expected)
            .Select(e => e.Name.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static List<string> DataIssueAlertIds(XElement root)
        => root.Element(ReportNamespace + "Alerts")
            ?.Elements(ReportNamespace + "Alert")
            .Where(alert => string.Equals(alert.Attribute("Name")?.Value, "DataIssue", StringComparison.Ordinal))
            .SelectMany(alert => alert.Elements(ReportNamespace + "Issue"))
            .Select(issue => issue.Attribute("Id")?.Value)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList()
            ?? [];

    private static DeployReportClassification Unclassifiable(string reason)
        => DeployReportClassification.Unclassifiable([reason]);

    private sealed record FlaggedItem(string OperationName, string ItemValue, string? IssueId, bool IsNeverDestructive);
}
