using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Reference search value to a ParamSource over ReferenceSearchParam. Distinguishes local
/// references (<c>BaseUri IS NULL</c>) from external ones (<c>BaseUri = @p COLLATE BIN2</c>) via the
/// shared <see cref="ReferenceColumnEquality"/> helper. ReferenceResourceVersion remains outside identity.
/// </summary>
/// <remarks>
/// <see cref="SearchModifierCode.Type"/> is the one modifier accepted here, and it is accepted as a no-op:
/// the binder folds the named type into the <see cref="ReferenceSearchValue"/> itself, so
/// <see cref="ReferenceColumnEquality"/> already applies it from the value and the modifier carries no
/// further meaning. Rejecting it would break <c>subject:Patient=123</c>. Every other modifier throws
/// rather than lower to plain equality: <c>:identifier</c> in particular is rewritten to a token predicate
/// against a derived search parameter well before this point, so reaching here means hand-built IR, and
/// silently dropping it would widen the match to every reference regardless of identifier — wrong rows,
/// no diagnostic. Same reasoning as <see cref="TokenLoweringRule"/>'s refusal.
/// </remarks>
internal static class ReferenceLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, ReferenceSearchValue value, LeafContext context, short? resourceTypeId)
    {
        if (predicate.Modifier?.SearchModifierCode is { } modifier &&
            modifier != SearchModifierCode.Type)
        {
            throw new NotSupportedException($"Reference search does not support the ':{modifier}' modifier.");
        }

        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var combined = ReferenceColumnEquality.Build(
            table, "BaseUri", "ReferenceResourceTypeId", "ReferenceResourceId", value, context, predicate.Parameter);

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), combined);
    }
}
