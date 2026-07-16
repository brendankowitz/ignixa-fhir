using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Token search value to a ParamSource over TokenSearchParam. Code-only case only --
/// system-qualified tokens need SystemId resolution, which ISymbolResolver does not support yet, so
/// they throw rather than silently ignoring the system filter (a silent wrong-answer would be worse
/// than a loud failure).
/// </summary>
public static class TokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, TokenSearchValue value, LeafContext context)
    {
        if (!string.IsNullOrEmpty(value.System))
        {
            throw new NotSupportedException(
                "System-qualified token search requires SystemId resolution, which ISymbolResolver does not " +
                "support yet -- see docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-phase4-5-ast-emit-lower.md task 7's scope note.");
        }

        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var column = new SqlColumnRef(table.TableName, "Code");
        var predicateExpr = new Predicate.Equal(column, context.Parameter(value.Code!));

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
