using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>Lowers a compartment membership set — the structural counterpart of
/// <see cref="CompartmentLoweringRule"/>, which emits one membership group's CTE. Shared by an ordinary
/// compartment search and by <c>$everything</c>'s compartment branch.</summary>
internal static class CompartmentSetLoweringRule
{
    /// <summary>Lowers a compartment membership set to a Union of one CompartmentSource per membership search parameter,
    /// narrowing member types to <paramref name="filteredResourceTypes"/> when non-empty. Shared by an ordinary
    /// compartment search and <c>$everything</c>. A filter that narrows membership to zero groups lowers to an
    /// empty match (a <see cref="Predicate.False"/>), following the "not found is data, not an error" convention.</summary>
    public static CteRef Lower(
        string compartmentType,
        string compartmentId,
        ISet<string> filteredResourceTypes,
        StructuralContext context)
    {
        var membership = context.LeafContext.CompartmentMembership(compartmentType);
        var groups = filteredResourceTypes.Count == 0
            ? membership
            : membership
                .Select(m => (m.Parameter, ResourceTypes: (IReadOnlyList<string>)m.ResourceTypes.Where(filteredResourceTypes.Contains).ToList()))
                .Where(m => m.ResourceTypes.Count > 0)
                .ToList();

        if (groups.Count == 0)
        {
            // Named only types outside this compartment, so the answer is an empty member set, not an exception.
            // Anchor the false predicate on the compartment's own type so the CTE still emits well-typed SQL.
            var reason =
                $"Compartment search for '{compartmentType}/{compartmentId}' resolved to " +
                "zero membership search parameters for the requested resource type(s) -- this compartment/filter " +
                "combination can never match any row.";

            return context.LowerResourceSourceWithPredicate(compartmentType, new Predicate.False(reason));
        }

        var refs = groups.Select(g =>
            context.Graph.Add(CompartmentLoweringRule.Lower(g.Parameter, g.ResourceTypes, compartmentType, compartmentId, context.LeafContext))).ToList();

        return context.Union(refs);
    }
}
