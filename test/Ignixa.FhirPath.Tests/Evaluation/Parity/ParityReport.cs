/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Renders a sweep's divergences as the text that backs the committed inventory.
 */

using System.Globalization;
using System.Text;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Turns raw divergences into a grouped, readable inventory.
/// </summary>
/// <remarks>
/// The committed inventory in docs is written by hand from this output, because the parts that matter
/// most - which behaviour is spec-correct, and what the seam would have to do about it - are
/// judgements a generator cannot make. What the generator is for is keeping the raw facts honest and
/// making the document cheap to regenerate when the engine changes.
/// </remarks>
internal static class ParityReport
{
    public static string Render(IReadOnlyList<ParityDivergence> divergences, int expressions, int resources)
    {
        ArgumentNullException.ThrowIfNull(divergences);

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"# Firely 5.11.4 vs Ignixa - {divergences.Count} divergence(s)");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"{expressions} expression(s) x {resources} resource(s) = {expressions * resources} evaluations per engine.");
        report.AppendLine();

        foreach (var group in divergences.GroupBy(divergence => divergence.Signature).OrderByDescending(group => group.Count()))
        {
            AppendGroup(report, group);
        }

        return report.ToString();
    }

    private static void AppendGroup(StringBuilder report, IGrouping<string, ParityDivergence> group)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"## {group.Key} ({group.Count()})");
        report.AppendLine();

        foreach (var divergence in group.Take(40))
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"- `{divergence.Expression}` on {divergence.ResourceName} [{divergence.Source}]");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  - firely: {divergence.Firely.Describe()}");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  - ignixa: {divergence.Ignixa.Describe()}");
        }

        if (group.Count() > 40)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- ... and {group.Count() - 40} more");
        }

        report.AppendLine();
    }
}
