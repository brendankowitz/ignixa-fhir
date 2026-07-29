using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a <c>:text</c> search to a ParamSource over dbo.TokenText. The token tables index a token's
/// system and code; its human-readable text lives in its own table, so <c>:text</c> is the one token
/// modifier that changes which table is read rather than which column is compared.
/// <para>
/// No COLLATE is emitted: TokenText.Text is declared Latin1_General_CI_AI in the DDL, so the column's own
/// collation already gives <c>:text</c> its specified case- and accent-insensitive matching, and forcing a
/// collation would make the predicate non-sargable against that table's index.
/// </para>
/// </summary>
internal static class TokenTextLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo parameter,
        StringExpression expression,
        LeafContext context,
        short resourceTypeId)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (expression.FieldName != FieldName.TokenText)
        {
            throw new NotSupportedException(
                $"A StringExpression over {expression.FieldName} reached TokenText lowering. Only " +
                "FieldName.TokenText is stored in dbo.TokenText; lowering any other field there would " +
                "search the wrong table and silently return the wrong rows.");
        }

        var match = expression.StringOperator switch
        {
            StringOperator.StartsWith => LikeMatch.StartsWith,
            StringOperator.Contains => LikeMatch.Contains,
            _ => throw new NotSupportedException(
                $"StringOperator.{expression.StringOperator} is not supported for :text -- only StartsWith " +
                "and Contains have a LikeMatch equivalent."),
        };

        var table = SqlCatalog.Default.Table("TokenText");
        return new CteDefinition.ParamSource(
            table,
            resourceTypeId,
            context.SearchParamId(parameter),
            new Predicate.Like(new SqlColumnRef(table.TableName, "Text"), context.Parameter(expression.Value), match));
    }
}
