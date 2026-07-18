using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Token search value to a ParamSource over TokenSearchParam. Code-only case only --
/// system-qualified tokens (including System = string.Empty, meaning "system must be absent") need
/// SystemId resolution, which ISymbolResolver does not support yet, and text-only tokens (no Code)
/// have no column to compare against here. Both cases throw rather than silently producing a
/// wrong-scope or always-false predicate (a silent wrong-answer would be worse than a loud failure).
/// </summary>
public static class TokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, TokenSearchValue value, LeafContext context, short resourceTypeId)
    {
        if (value.System is not null)
        {
            throw new NotSupportedException(
                "System-qualified token search requires SystemId resolution, which ISymbolResolver does not " +
                "support yet -- see docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-phase4-5-ast-emit-lower.md task 7's scope note. " +
                "This includes System = string.Empty (\"|code\" syntax, meaning system must be absent), which this rule cannot express either.");
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
