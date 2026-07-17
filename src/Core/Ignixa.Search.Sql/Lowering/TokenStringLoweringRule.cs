using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenString composite to a single ParamSource over TokenStringCompositeSearchParam --
/// components[0] is the token slot (Code1, via TokenColumnEquality), components[1] is the string slot
/// (Text2/TextOverflow2). Composite components never carry a SearchModifier
/// (SearchExpressionBinder.BindComposite always passes modifier: null per component -- confirmed by
/// reading its source), so unlike StringLoweringRule this rule has exactly one case: the default/
/// no-modifier StartsWith semantics, which per StringLoweringRule's own doc comment is safe against
/// the inline column alone in both the overflowed and non-overflowed case -- no throw guard needed.
/// The collation is TokenStringCompositeSearchParam's own (Latin1_General_CI_AI), NOT
/// StringSearchParam.Text's (Latin1_General_100_CI_AI) -- a real, DDL-confirmed divergence.
/// </summary>
public static class TokenStringLoweringRule
{
    private const string CaseInsensitiveCollation = "Latin1_General_CI_AI";

    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenStringCompositeSearchParam");
        var tokenPredicate = TokenColumnEquality.Build(table, "Code1", (TokenSearchValue)components[0].Value, context);
        var stringPredicate = StringColumnStartsWith(table, (StringSearchValue)components[1].Value, context);

        var predicate = new Predicate.And(tokenPredicate, stringPredicate);
        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate StringColumnStartsWith(TableDescriptor table, StringSearchValue value, LeafContext context)
    {
        var inlineWidth = table.Column("Text2").MaxLength
            ?? throw new InvalidOperationException("TokenStringCompositeSearchParam.Text2 has no MaxLength in SqlCatalog.");

        var usesTextColumn = value.String.Length <= inlineWidth;
        var column = new SqlColumnRef(table.TableName, usesTextColumn ? "Text2" : "TextOverflow2");
        return new Predicate.Like(column, context.Parameter(value.String), LikeMatch.StartsWith, CaseInsensitiveCollation);
    }
}
