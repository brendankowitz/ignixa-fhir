using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Token search value to a ParamSource over TokenSearchParam, code-only. A system-qualified
/// token (including the system-must-be-absent "|code" form) needs SystemId resolution, which
/// ISymbolResolver does not support yet, and a text-only token has no code column to compare — both throw
/// rather than silently produce a wrong-scope or always-false predicate.
/// </summary>
public static class TokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, TokenSearchValue value, LeafContext context, short resourceTypeId)
    {
        if (value.System is not null)
        {
            throw new NotSupportedException(
                "System-qualified token search requires SystemId resolution, which ISymbolResolver does not " +
                "support yet. This includes System = string.Empty (\"|code\" syntax, meaning system must be absent), " +
                "which this rule cannot express either.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException(
                "This rule only supports code-bearing tokens -- text-only tokens (TokenSearchValue.Code is null/empty) " +
                "are not supported yet.");
        }

        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var column = new SqlColumnRef(table.TableName, "Code");
        var predicateExpr = new Predicate.Equal(column, context.Parameter(value.Code));

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
