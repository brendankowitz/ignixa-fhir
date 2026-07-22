using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Token search value to a ParamSource over TokenSearchParam with full FHIR qualifier support:
/// bare code, |code, system|, system|code, unknown system → false. A text-only token (no system, no code)
/// retains its throw because display text is not a code.
/// </summary>
public static class TokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, TokenSearchValue value, LeafContext context, short resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var predicateExpr = TokenColumnEquality.Build(table, "SystemId", "Code", value, context);

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
