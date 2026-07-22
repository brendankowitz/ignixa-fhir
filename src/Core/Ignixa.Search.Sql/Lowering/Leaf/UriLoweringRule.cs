using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Uri search value to a ParamSource over UriSearchParam.
/// <list type="bullet">
///   <item>No modifier: exact case-sensitive equality (binary collation).</item>
///   <item>:below — lexical prefix match via LIKE StartsWith (binary collation; SqlBuilder escapes LIKE metacharacters).</item>
///   <item>:above — reverse prefix via PrefixOfParameter (binary collation; full raw URI bound as parameter).</item>
/// </list>
/// Version/Fragment are not part of the base UriSearchParam table; this rule covers the base Uri column only.
/// URI hierarchy parity is lexical prefix, case-sensitive, not segment-aware.
/// </summary>
public static class UriLoweringRule
{
    private const string BinaryCollation = "Latin1_General_100_BIN2";

    public static CteDefinition.ParamSource Lower(
        SearchParameterPredicateExpression predicate,
        UriSearchValue value,
        LeafContext context,
        short resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("UriSearchParam");
        var column = new SqlColumnRef(table.TableName, "Uri");

        Predicate predicateExpr = predicate.Modifier?.SearchModifierCode switch
        {
            null => new Predicate.Equal(column, context.Parameter(value.Uri), BinaryCollation),
            SearchModifierCode.Below => new Predicate.Like(
                column, context.Parameter(value.Uri), LikeMatch.StartsWith, BinaryCollation),
            SearchModifierCode.Above => new Predicate.PrefixOfParameter(
                column, context.Parameter(value.Uri), BinaryCollation),
            var modifier => throw new NotSupportedException(
                $"Uri search does not support the ':{modifier}' modifier."),
        };

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
