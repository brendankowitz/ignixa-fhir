using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Reference search value to a ParamSource over ReferenceSearchParam. Distinguishes local
/// references (<c>BaseUri IS NULL</c>) from external ones (<c>BaseUri = @p COLLATE BIN2</c>) via the
/// shared <see cref="ReferenceColumnEquality"/> helper. ReferenceResourceVersion remains outside identity.
/// </summary>
public static class ReferenceLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, ReferenceSearchValue value, LeafContext context, short resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var combined = ReferenceColumnEquality.Build(
            table, "BaseUri", "ReferenceResourceTypeId", "ReferenceResourceId", value, context, predicate.Parameter);

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), combined);
    }
}
