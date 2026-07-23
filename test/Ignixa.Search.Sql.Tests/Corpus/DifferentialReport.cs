using System.Globalization;
using System.Text;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>Renders the differential run as markdown for human triage.</summary>
public static class DifferentialReport
{
    public static string Render(IReadOnlyList<DifferentialResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var report = new StringBuilder();
        report.AppendLine("# Legacy SQL differential report");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"{results.Count} captured searches, each compiled and compared against the SQL the shipping engine executed.");
        report.AppendLine();

        AppendSummary(report, results);
        AppendGaps(report, results);
        AppendDivergences(report, results);

        return report.ToString();
    }

    private static void AppendSummary(StringBuilder report, IReadOnlyList<DifferentialResult> results)
    {
        report.AppendLine("## Summary");
        report.AppendLine();
        report.AppendLine("| Verdict | Count |");
        report.AppendLine("|---|---:|");

        foreach (var verdict in Enum.GetValues<ShapeVerdict>())
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"| {verdict} | {results.Count(r => r.Verdict == verdict)} |");
        }

        report.AppendLine();
    }

    private static void AppendGaps(StringBuilder report, IReadOnlyList<DifferentialResult> results)
    {
        var failures = results.Where(r => !r.Compilation.Succeeded).ToList();

        report.AppendLine("## Gaps -- queries the compiler cannot express");
        report.AppendLine();

        if (failures.Count == 0)
        {
            report.AppendLine("None.");
            report.AppendLine();
            return;
        }

        foreach (var group in failures.GroupBy(r => r.Compilation.FailureStage).OrderByDescending(g => g.Count()))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"### {group.Key} ({group.Count()})");
            report.AppendLine();

            foreach (var reason in group.GroupBy(r => r.Compilation.FailureMessage).OrderByDescending(g => g.Count()))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- **{reason.Count()}x** {Escape(reason.Key)}");
                foreach (var result in reason.Take(3))
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"  - `{result.Entry.Url}`");
                }
            }

            report.AppendLine();
        }
    }

    private static void AppendDivergences(StringBuilder report, IReadOnlyList<DifferentialResult> results)
    {
        var divergent = results
            .Where(r => r.Comparison is not null && r.Verdict != ShapeVerdict.Match)
            .OrderBy(r => r.Verdict)
            .ThenBy(r => r.Entry.Url, StringComparer.Ordinal)
            .ToList();

        report.AppendLine("## Divergences -- compiled, but asks the database for something different");
        report.AppendLine();

        if (divergent.Count == 0)
        {
            report.AppendLine("None.");
            report.AppendLine();
            return;
        }

        foreach (var result in divergent)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"### {result.Verdict}: `{result.Entry.Url}`");
            report.AppendLine();

            AppendList(report, "Only the shipping engine does", result.Comparison!.OnlyInLegacy);
            AppendList(report, "Only the compiler does", result.Comparison.OnlyInCompiler);
            AppendList(report, "Operator differences (encoding, not semantics)", result.Comparison.OperationDifferences);

            report.AppendLine("<details><summary>shapes</summary>");
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine("legacy:");
            report.AppendLine(result.Legacy.Describe());
            report.AppendLine();
            report.AppendLine("compiler:");
            report.AppendLine(result.Compiled!.Describe());
            report.AppendLine("```");
            report.AppendLine();
            report.AppendLine("</details>");
            report.AppendLine();
        }
    }

    private static void AppendList(StringBuilder report, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"{title}:");
        foreach (var item in items)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- `{item}`");
        }

        report.AppendLine();
    }

    private static string Escape(string? text)
        => (text ?? "(no message)").Replace("\n", " ", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);
}
