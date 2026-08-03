using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Uri search value to a ParamSource over UriSearchParam (base Uri column only; Version/Fragment
/// are not on this table). No modifier is exact case-sensitive equality; :below/:above match self or any
/// descendant/ancestor at a path-segment boundary — segment-aware, so <c>:below=.../ValueSet</c> does not
/// match a stored <c>.../ValueSetOther</c>.
/// </summary>
internal static class UriLoweringRule
{
    private const char SegmentSeparator = '/';

    public static CteDefinition.ParamSource Lower(
        SearchParameterPredicateExpression predicate,
        UriSearchValue value,
        LeafContext context,
        short? resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("UriSearchParam");
        var column = new SqlColumnRef(table.TableName, "Uri");

        Predicate predicateExpr = predicate.Modifier?.SearchModifierCode switch
        {
            null => new Predicate.Equal(column, context.Parameter(value.Uri)),

            SearchModifierCode.Below => new Predicate.Or(
                new Predicate.Equal(column, context.Parameter(value.Uri)),
                new Predicate.Like(column, context.Parameter(SegmentPrefix(value.Uri)), LikeMatch.StartsWith)),

            // The appended separator makes the search value one char longer than an exact-matching stored
            // value, so LEFT(@base + '/', LEN(col)) = col still admits the exact match while a same-prefix
            // sibling such as ".../ValueSetOther" fails the character-for-character comparison.
            SearchModifierCode.Above => new Predicate.PrefixOfParameter(
                column, context.Parameter(SegmentPrefix(value.Uri))),

            var modifier => throw new NotSupportedException(
                $"Uri search does not support the ':{modifier}' modifier."),
        };

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }

    /// <summary>
    /// The search value with exactly one trailing segment separator, so that a caller-supplied trailing
    /// slash does not produce a doubled one.
    /// </summary>
    private static string SegmentPrefix(string uri) => uri.TrimEnd(SegmentSeparator) + SegmentSeparator;
}
