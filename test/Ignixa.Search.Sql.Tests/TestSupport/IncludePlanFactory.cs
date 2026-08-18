using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Tests.TestSupport;

internal static class IncludePlanFactory
{
    public static QueryPlan Create(
        IReadOnlyList<CteDefinition> ctes,
        MatchPageSpec spec,
        IReadOnlyList<IncludeStage> includes,
        ResourceVisibility? visibility = null,
        ProjectionSpec? projection = null)
    {
        ArgumentNullException.ThrowIfNull(ctes);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(includes);

        if (includes.Count == 0)
        {
            throw new ArgumentException("An include plan requires at least one include stage.", nameof(includes));
        }

        if (spec.CountOnly)
        {
            return new QueryPlan(ctes, spec, Includes: includes, Visibility: visibility, Projection: projection);
        }

        List<CteDefinition> planCtes = new(ctes);
        var matchPage = new CteRef(planCtes.Count);
        planCtes.Add(new CteDefinition.MatchPage(spec));

        var includeSeed = matchPage;
        if (spec.TrimmedPageSize is not null && includes.Any(stage => stage.SeedFromMatch))
        {
            includeSeed = new CteRef(planCtes.Count);
            planCtes.Add(new CteDefinition.MatchSeed(matchPage, spec));
        }

        return new QueryPlan(
            planCtes,
            spec,
            Includes: includes,
            Visibility: visibility,
            Projection: projection,
            IncludeSeed: includeSeed);
    }
}
