// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// A search plan cut to a size budget, with what was cut in <see cref="Omitted"/> (<c>size-budget</c>). In order, until the
/// response fits: top values beyond three per definition, then all top values, then parameters from the end (with their own
/// warnings, which name them), then the remaining issues from the end. Deterministic: the same plan and budget cut the same.
/// </summary>
/// <param name="Facts">The plan as shown.</param>
/// <param name="Issues">The issues shown: the request's, then the plan's own.</param>
/// <param name="Omitted">What was left out.</param>
internal sealed record PlanFit(PlanFacts Facts, IReadOnlyList<IssueItem> Issues, IReadOnlyList<OmittedItem> Omitted)
{
    private const int ShortTopValues = 3;

    /// <param name="session">The session, for measuring the cache issues the response will carry.</param>
    /// <param name="facts">The whole plan.</param>
    /// <param name="requestIssues">Issues about the request and the indexes read, kept ahead of the plan's own.</param>
    /// <param name="planIssues">The plan's warnings, one per parameter at most, in parameter order.</param>
    /// <param name="omitted">What the caller already left out.</param>
    /// <param name="next">The response's <c>next</c>.</param>
    /// <param name="budget">The most bytes the response may measure.</param>
    public static PlanFit Within(FolderSession session, PlanFacts facts, IReadOnlyList<IssueItem> requestIssues, IReadOnlyList<IssueItem> planIssues, IReadOnlyList<OmittedItem> omitted, IReadOnlyList<NextCall> next, int budget)
    {
        ArgumentNullException.ThrowIfNull(facts);

        PlanFacts shortened = facts with { Parameters = [.. facts.Parameters.Select(parameter => TopValues(parameter, ShortTopValues))] };
        PlanFacts bare = facts with { Parameters = [.. facts.Parameters.Select(parameter => TopValues(parameter, 0))] };
        List<IssueItem> issues = [.. requestIssues, .. planIssues];
        (PlanFacts Facts, OmittedItem? Note)[] stages =
        [
            (facts, null),
            (shortened, new OmittedItem($"top values beyond {ShortTopValues} per parameter definition", "size-budget")),
            (bare, new OmittedItem("all top values", "size-budget")),
        ];
        foreach ((PlanFacts candidate, OmittedItem? note) in stages)
        {
            List<OmittedItem> cut = note is null ? [.. omitted] : [.. omitted, note];
            if (Fits(session, candidate, issues, cut, next, budget))
            {
                return new PlanFit(candidate, issues, cut);
            }
        }

        // Fewer parameters only ever shrink the response, so the most that fit is found by bisection.
        int low = 0;
        int high = Math.Max(0, bare.Parameters.Count - 1);
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Fits(session, Fewer(bare, middle), issues, Cut(omitted, bare, middle), next, budget))
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (Fits(session, Fewer(bare, low), issues, Cut(omitted, bare, low), next, budget))
        {
            return new PlanFit(Fewer(bare, low), issues, Cut(omitted, bare, low));
        }

        // Not even one parameter fits beside the issues: keep as many issues as fit.
        for (int kept = issues.Count - 1; kept >= 0; kept--)
        {
            List<OmittedItem> lessIssues = [.. Cut(omitted, bare, 0), new OmittedItem($"{issues.Count - kept} more issues", "size-budget")];
            if (Fits(session, Fewer(bare, 0), [.. issues.Take(kept)], lessIssues, next, budget) || kept == 0)
            {
                return new PlanFit(Fewer(bare, 0), [.. issues.Take(kept)], lessIssues);
            }
        }

        return new PlanFit(bare with { Parameters = [] }, [], [.. omitted, new OmittedItem($"{bare.Parameters.Count} parameters and {issues.Count} issues", "size-budget")]);
    }

    private static PlanFacts Fewer(PlanFacts bare, int listed) => bare with { Parameters = [.. bare.Parameters.Take(listed)] };

    private static List<OmittedItem> Cut(IReadOnlyList<OmittedItem> omitted, PlanFacts bare, int listed) => listed >= bare.Parameters.Count
        ? [.. omitted, new OmittedItem("all top values", "size-budget")]
        : [.. omitted, new OmittedItem("all top values", "size-budget"), new OmittedItem($"{bare.Parameters.Count - listed} more parameters, from '{QueryEcho.Value(bare.Parameters[listed].Name)}' on (check fewer at a time)", "size-budget")];

    private static bool Fits(FolderSession session, PlanFacts facts, IReadOnlyList<IssueItem> issues, IReadOnlyList<OmittedItem> omitted, IReadOnlyList<NextCall> next, int budget) =>
        EnvelopeAssembler.MaxSize<PlanFacts?>(session, facts, [], omitted, [], issues, next) <= budget;

    private static ParameterPlan TopValues(ParameterPlan parameter, int keep) => parameter with
    {
        Resolves = [.. parameter.Resolves.Select(binding => binding.TopValues is null ? binding : binding with { TopValues = keep == 0 ? null : [.. binding.TopValues.Take(keep)] })],
    };
}
