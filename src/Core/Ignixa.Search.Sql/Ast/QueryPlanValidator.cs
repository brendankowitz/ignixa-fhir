namespace Ignixa.Search.Sql.Ast;

internal static class QueryPlanValidator
{
    internal static void Validate(QueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.MatchSpec is null)
        {
            throw new NotSupportedException(
                "QueryPlan.MatchSpec is required so the match root and wrapper CTEs have a canonical specification.");
        }

        RequireIndex(plan.Match.Index, plan.Ctes.Count, ReferenceBound.Defined, "QueryPlan.Match");
        var matchPageCount = 0;
        var matchPageIndex = -1;
        var matchSeedCount = 0;
        var matchSeedIndex = -1;

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            switch (plan.Ctes[i])
            {
                case CteDefinition.Intersect(var left, var right):
                    RequireChild(left, i, "Left");
                    RequireChild(right, i, "Right");
                    break;
                case CteDefinition.Except(var exceptLeft, var exceptRight):
                    RequireChild(exceptLeft, i, "Left");
                    RequireChild(exceptRight, i, "Right");
                    break;
                case CteDefinition.Union(var parts):
                    for (var part = 0; part < parts.Count; part++)
                    {
                        RequireChild(parts[part], i, "Parts", part);
                    }

                    break;
                case CteDefinition.ChainJoin chain:
                    RequireChild(chain.InnerMatch, i, "InnerMatch");
                    break;
                case CteDefinition.ReferencedTypeExpansion expansion:
                    RequireChild(expansion.Seed, i, "Seed");
                    break;
                case CteDefinition.MatchPage page:
                    RequireCanonicalSpec(page.Spec, plan.MatchSpec, i);
                    RequireChild(page.Spec.Root, i, nameof(CteDefinition.MatchPage.Spec));
                    matchPageCount++;
                    matchPageIndex = i;
                    break;
                case CteDefinition.MatchSeed seed:
                    RequireCanonicalSpec(seed.Spec, plan.MatchSpec, i);
                    RequireChild(seed.Page, i, nameof(CteDefinition.MatchSeed.Page));
                    RequireMatchPage(plan.Ctes[seed.Page.Index], i);
                    matchSeedCount++;
                    matchSeedIndex = i;
                    break;
            }
        }

        RejectDanglingIncludeReferences(plan);

        if (plan.CountOnly && plan.IncludeSeed is not null)
        {
            throw new NotSupportedException(
                "CountOnly plans cannot carry IncludeSeed because count rendering reads the pre-page match root directly.");
        }

        var hasWrapper = matchPageCount > 0 || matchSeedCount > 0;
        if (plan.CountOnly && hasWrapper)
        {
            throw new NotSupportedException(
                "CountOnly plans cannot carry MatchPage or MatchSeed wrapper CTEs; count rendering reads the pre-page match root directly.");
        }

        if (plan.Includes is { Count: > 0 } && !plan.CountOnly)
        {
            var canonicalIncludeSeed = RequireCanonicalWrapperTail(
                plan,
                matchPageCount,
                matchPageIndex,
                matchSeedCount,
                matchSeedIndex);
            RequireIncludeSeed(plan, canonicalIncludeSeed);
        }
        else
        {
            if (hasWrapper)
            {
                throw new NotSupportedException(
                    "MatchPage and MatchSeed wrapper CTEs require at least one include stage on a non-count plan.");
            }

            if (plan.IncludeSeed is not null)
            {
                throw new NotSupportedException(
                    "QueryPlan.IncludeSeed requires at least one include stage on a non-count plan.");
            }
        }

        static void RequireChild(CteRef reference, int ordinal, string member, int part = -1)
            => RequireIndex(reference.Index, ordinal, ReferenceBound.EarlierCte, "Ctes", ordinal, member, part);
    }

    private static CteRef RequireCanonicalWrapperTail(
        QueryPlan plan,
        int matchPageCount,
        int matchPageIndex,
        int matchSeedCount,
        int matchSeedIndex)
    {
        if (matchPageCount != 1)
        {
            throw new NotSupportedException(
                $"An include plan requires exactly one MatchPage wrapper CTE, but has {matchPageCount}.");
        }

        if (matchSeedCount > 1)
        {
            throw new NotSupportedException(
                $"An include plan can carry at most one MatchSeed wrapper CTE, but has {matchSeedCount}.");
        }

        if (matchSeedCount == 0)
        {
            if (matchPageIndex != plan.Ctes.Count - 1)
            {
                throw new NotSupportedException(
                    "MatchPage must be the final CTE in the canonical wrapper tail when MatchSeed is absent.");
            }

            return new CteRef(matchPageIndex);
        }

        if (matchPageIndex != plan.Ctes.Count - 2 || matchSeedIndex != matchPageIndex + 1)
        {
            throw new NotSupportedException(
                "MatchPage and MatchSeed must form the final canonical wrapper tail in that order.");
        }

        if (plan.MatchSpec.OffsetPage is not { ProbeExtraRow: true })
        {
            throw new NotSupportedException(
                "MatchSeed requires an OffsetPage with ProbeExtraRow enabled.");
        }

        if (!plan.Includes!.Any(stage => stage.SeedFromMatch))
        {
            throw new NotSupportedException(
                "MatchSeed requires at least one include stage with SeedFromMatch enabled.");
        }

        return new CteRef(matchSeedIndex);
    }

    private static void RequireCanonicalSpec(MatchPageSpec? candidate, MatchPageSpec canonical, int ordinal)
    {
        if (!ReferenceEquals(candidate, canonical))
        {
            throw new NotSupportedException(
                $"Ctes[{ordinal}].Spec must reference the QueryPlan's canonical MatchPageSpec instance.");
        }
    }

    private static void RequireMatchPage(CteDefinition target, int ordinal)
    {
        if (target is not CteDefinition.MatchPage)
        {
            throw new NotSupportedException(
                $"Ctes[{ordinal}].Page must reference a MatchPage CTE.");
        }
    }

    private static void RequireIncludeSeed(QueryPlan plan, CteRef canonicalIncludeSeed)
    {
        if (plan.IncludeSeed is not { } includeSeed)
        {
            throw new NotSupportedException(
                "QueryPlan.IncludeSeed is required when a non-count plan has include stages.");
        }

        RequireIndex(includeSeed.Index, plan.Ctes.Count, ReferenceBound.Defined, "QueryPlan.IncludeSeed");

        if (includeSeed.Index != canonicalIncludeSeed.Index)
        {
            throw new NotSupportedException(
                $"QueryPlan.IncludeSeed must reference the canonical {(plan.Ctes[canonicalIncludeSeed.Index] is CteDefinition.MatchSeed ? "MatchSeed" : "MatchPage")} wrapper CTE.");
        }
    }

    private static void RejectDanglingIncludeReferences(QueryPlan plan)
    {
        if (plan.Includes is not { Count: > 0 } includes)
        {
            return;
        }

        for (var i = 0; i < includes.Count; i++)
        {
            var stage = includes[i];
            if (!stage.SeedFromMatch && stage.SeedStages is not { Count: > 0 })
            {
                throw new NotSupportedException(
                    $"Includes[{i}] must set SeedFromMatch or SeedStages must name at least one earlier stage.");
            }

            foreach (var seed in stage.SeedStages)
            {
                RequireIndex(seed, i, ReferenceBound.EarlierStage, "Includes", i, "SeedStages");
            }

            foreach (var constraint in stage.Constraints ?? [])
            {
                RequireIndex(
                    constraint.ConstraintCteIndex,
                    plan.Ctes.Count,
                    ReferenceBound.Defined,
                    "Includes",
                    i,
                    "Constraints");
            }
        }
    }

    private enum ReferenceBound
    {
        Defined,
        EarlierCte,
        EarlierStage,
    }

    private static void RequireIndex(
        int index,
        int exclusiveUpperBound,
        ReferenceBound bound,
        string owner,
        int ownerOrdinal = -1,
        string? member = null,
        int memberOrdinal = -1)
    {
        if (index >= 0 && index < exclusiveUpperBound)
        {
            return;
        }

        var path = ownerOrdinal < 0 ? owner : $"{owner}[{ownerOrdinal}].{member}";
        if (memberOrdinal >= 0)
        {
            path += $"[{memberOrdinal}]";
        }

        var limit = bound switch
        {
            ReferenceBound.Defined => $"the plan defines {exclusiveUpperBound} CTEs",
            ReferenceBound.EarlierCte => $"a CTE may only reference the {exclusiveUpperBound} emitted before it",
            _ => $"a stage may only seed from the {exclusiveUpperBound} emitted before it",
        };

        throw new NotSupportedException(
            $"{path} references index {index}, but {limit}. A plan whose CTE graph is not closed and ordered " +
            "cannot be emitted; a rewritten plan must renumber the references it moved.");
    }
}
