namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The kind tokens carried by <see cref="PlanExplainRow.Kind"/> — which <c>CteDefinition</c> case (or
/// non-CTE section) produced the row, kept as data so a consumer needn't recover it by prefix-matching
/// prose. Tokens, not an enum (like <see cref="Builders.SqlRangeKind"/>), since renderers consume them across a wire.
/// </summary>
public static class PlanRowKind
{
    public const string ParamSource = "paramSource";

    public const string Intersect = "intersect";

    public const string Union = "union";

    public const string ResourceSource = "resourceSource";

    public const string Except = "except";

    public const string ChainJoin = "chainJoin";

    public const string CompartmentSource = "compartmentSource";

    public const string NotReferencedSource = "notReferencedSource";

    public const string MultiTypeResourceSource = "multiTypeResourceSource";

    public const string TableExistsPredicate = "tableExistsPredicate";

    public const string VisibleSinceFilter = "visibleSinceFilter";

    public const string ReferencedTypeExpansion = "referencedTypeExpansion";

    public const string IncludeStage = "includeStage";

    public const string IncludeBoundary = "includeBoundary";

    public const string SortSpec = "sortSpec";

    public const string PageSpec = "pageSpec";

    public const string OffsetSpec = "offsetSpec";

    public const string CountOnly = "countOnly";
}
