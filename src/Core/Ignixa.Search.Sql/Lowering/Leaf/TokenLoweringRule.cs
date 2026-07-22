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
/// <remarks>
/// Only the unmodified form is implemented here. <c>:not</c> never reaches this rule — <c>Lower</c>
/// rewrites it into a negation before leaf dispatch — and <c>:missing</c> lowers through its own node kind.
/// Every remaining token modifier (<c>:text</c>, <c>:in</c>, <c>:not-in</c>, <c>:of-type</c>,
/// <c>:above</c>, <c>:below</c>, <c>:identifier</c>) needs either a different table or a terminology
/// expansion this compiler does not perform, so each throws rather than silently degrading to plain
/// equality and returning wrong rows.
/// </remarks>
public static class TokenLoweringRule
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
