using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Token search value to a ParamSource over TokenSearchParam (bare code, |code, system|,
/// system|code, unknown system → false; text-only throws — display text is not a code). Only the
/// unmodified form is handled here: <c>:not</c> is rewritten by <c>Lower</c> before dispatch, <c>:missing</c>
/// uses its own node kind, and every other modifier throws rather than degrade to plain equality.
/// </summary>
internal static class TokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, TokenSearchValue value, LeafContext context, short? resourceTypeId)
    {
        if (predicate.Modifier?.SearchModifierCode is { } modifier)
        {
            throw new NotSupportedException($"Token search does not support the ':{modifier}' modifier.");
        }

        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var predicateExpr = TokenColumnEquality.Build(table, "SystemId", "Code", "CodeOverflow", value, context);

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
