using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Composite;

/// <summary>
/// Lowers a TokenString composite to a single ParamSource over TokenStringCompositeSearchParam —
/// components[0] is the token slot (Code1, via <see cref="TokenColumnEquality"/>), components[1] is the
/// string slot (Text2/TextOverflow2). Composite components never carry a modifier, so this rule has only
/// the default StartsWith case, which is always safe against the inline column (see
/// <see cref="Leaf.StringLoweringRule"/>). The collation is this composite table's own
/// (Latin1_General_CI_AI), which differs from StringSearchParam.Text's — a real, DDL-confirmed divergence.
/// </summary>
public static class TokenStringLoweringRule
{
    private const string CaseInsensitiveCollation = "Latin1_General_CI_AI";

    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context,
        short? resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("TokenStringCompositeSearchParam");
        var tokenPredicate = TokenColumnEquality.Build(table, "SystemId1", "Code1", "CodeOverflow1", (TokenSearchValue)components[0].Value, context);
        var stringPredicate = StringColumnStartsWith(table, (StringSearchValue)components[1].Value, context);

        var predicate = new Predicate.And(tokenPredicate, stringPredicate);
        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(compositeParameter), predicate);
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
