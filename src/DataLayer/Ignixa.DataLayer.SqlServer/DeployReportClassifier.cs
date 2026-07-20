using System.Xml.Linq;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Classifies a SqlPackage/DacFx DeployReport as safe to auto-apply unattended, or not.
/// Create and Refresh operations are never destructive by construction (Create fails loudly
/// at deploy time rather than silently corrupting; Refresh only recompiles a procedure's
/// schema binding). Drop/Alter/TableRebuild/UnbindTable operations must match an explicit
/// allow-list, seeded from Phase B's own proven-benign DeployReport findings (Categories
/// B/C/D/E -- see docs/superpowers/plans/2026-07-19-ignixa-datalayer-sqlserver-phase-c.md's
/// Global Constraints for the full rationale behind each entry).
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
                var type = item.Attribute("Type")?.Value ?? string.Empty;
                var value = item.Attribute("Value")?.Value ?? string.Empty;

                if (!IsAllowListed(type, value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsAllowListed(string type, string value)
    {
        // Category B/E: default-value canonicalization noise -- never destructive to
        // existing rows, only affects future inserts.
        if (type == "SqlDefaultConstraint")
        {
            return true;
        }

        // Category C: hex-literal check-constraint canonicalization, proven specific to
        // this one constraint -- narrow, name-matched.
        if (type == "SqlCheckConstraint" && value.Contains("CH_Resource_RawResource_Length", StringComparison.Ordinal))
        {
            return true;
        }

        // Category D: the partition-function/scheme rebuild the post-deployment script's
        // imperative splitting causes on every non-empty-target comparison.
        if ((type == "SqlPartitionScheme" || type == "SqlPartitionFunction")
            && (value.Contains("PartitionScheme_ResourceChangeData_Timestamp", StringComparison.Ordinal)
                || value.Contains("PartitionFunction_ResourceChangeData_Timestamp", StringComparison.Ordinal)))
        {
            return true;
        }

        if (type == "SqlTable" && value.Contains("[dbo].[ResourceChangeData]", StringComparison.Ordinal))
        {
            return true;
        }

        // Category F: discovered empirically by Task 8's real older-schema (commit 0db642e3)
        // upgrade test, not a synthetic fixture. Phase B's Task 9 (commit d7e7c600) added six
        // nullable import-tracking columns to an existing table, dbo.PackageResource. DacFx
        // reports this as a table-level "Alter"/"SqlTable" item -- it does not surface individual
        // column-level Add/Drop items the way it does for a genuine destructive change (compare:
        // a real column drop instead produces an accompanying <Issue> cross-reference into
        // <Alerts><Alert Name="DataIssue">, confirmed via manual DeployReport generation against a
        // database with an extra, undeclared column -- see docs/superpowers/sdd/task-8-report.md).
        // This specific Alter carries no such DataIssue alert -- verified purely additive
        // (ContentHash, ImportCompletedDate, ImportErrorMessage, ImportStartDate,
        // ImportedConceptCount, TerminologyImportStatus, all NULL, no drops). Narrow and
        // name-matched like Categories C/D, not a general "any Alter is safe" rule: a future
        // migration that alters a *different* table still needs its own allow-list entry (or the
        // classifier needs the more general DataIssue-alert-based signal this discovery points
        // to -- flagged in Task 8's report as a design decision, not applied here).
        if (type == "SqlTable" && value.Contains("[dbo].[PackageResource]", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
