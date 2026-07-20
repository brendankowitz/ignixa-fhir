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

        return false;
    }
}
