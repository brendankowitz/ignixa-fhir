namespace Ignixa.Search.Sql.Ast;

/// <summary>Which rows a <see cref="ResultShape.Count"/> covers.</summary>
public enum CountScope
{
    /// <summary>
    /// Every matching resource, ignoring any sort the plan carries. This is the FHIR <c>Bundle.total</c>.
    /// </summary>
    AllMatches = 0,

    /// <summary>
    /// Only the segment <see cref="QueryPlan.Sort"/> names: the count joins the primary sort key, or applies
    /// its <c>NOT EXISTS</c> under <see cref="SortPhase.MissingPrimary"/>. It answers "how many rows are in
    /// the segment I am currently paging", and requires the plan to carry at least one sort key.
    /// </summary>
    CurrentSortPhase = 1,
}
