using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>Lowers a chained (<c>subject.name</c>) or reverse-chained (<c>_has</c>) search to a ChainJoin CTE:
/// the inner expression lowered against the chain's own scoping type, then joined through the reference
/// parameter to the output side's types. The depth guard around this rule lives on
/// <see cref="StructuralContext.LowerChain"/>, which is the only caller.</summary>
internal static class ChainLoweringRule
{
    /// <summary>Lowers one chain level. A chain names its own types on both sides, so no ambient resource-type
    /// scope is read or passed on: the inner expression is scoped by the chain's referencing type (reverse) or
    /// target type (forward), and the opposite side becomes the join's output type filter.</summary>
    public static CteRef Lower(
        ChainedExpression chain,
        StructuralContext context,
        AccessConstraintApplier accessConstraints,
        Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        if (chain.Reversed)
        {
            var referencingResourceType = chain.ResourceTypes switch
            {
                [var single] => single,
                _ => throw new NotSupportedException(
                    $"Reverse chain's referencing side resolved to {chain.ResourceTypes.Length} types -- a reverse chain " +
                    "scopes its inner expression against exactly one referencing type, and the real binder binds it " +
                    "that way (SearchKeyBinder.BindReverse's syntax.SourceResourceType). This is the only guard on " +
                    "that shape for IR built directly against the compiler API, so it refuses rather than guessing."),
            };

            var innerMatch = lowerNode(chain.Expression, context, referencingResourceType);
            innerMatch = accessConstraints.Apply(innerMatch, referencingResourceType, context, lowerNode);
            var referenceSearchParamId = context.LeafContext.SearchParamId(chain.ReferenceSearchParameter);
            var innerResourceTypeId = context.LeafContext.ResourceTypeId(referencingResourceType);
            var outputResourceTypeIds = chain.TargetResourceTypes switch
            {
                { Length: > 0 } targets => targets.Select(context.LeafContext.ResourceTypeId).ToList(),
                _ => throw new NotSupportedException(EmptyOutputSideMessage("Reverse", "target", "SearchKeyBinder.BindReverse")),
            };

            return context.Graph.Add(new CteDefinition.ChainJoin(innerMatch, referenceSearchParamId, innerResourceTypeId, outputResourceTypeIds, ChainDirection.Reverse));
        }

        var targetResourceType = chain.TargetResourceTypes switch
        {
            [var single] => single,
            _ => throw new NotSupportedException(
                $"Forward chain resolved to {chain.TargetResourceTypes.Length} candidate target types -- a forward chain " +
                "scopes its inner expression against exactly one target type, and the real binder resolves it that way " +
                "before this point (SearchKeyBinder.BindForward throws ChainedParameterSpecifyType on genuine " +
                "ambiguity). This is the only guard on that shape for IR built directly against the compiler API, so " +
                "it refuses rather than guessing."),
        };

        var forwardInnerMatch = lowerNode(chain.Expression, context, targetResourceType);
        forwardInnerMatch = accessConstraints.Apply(forwardInnerMatch, targetResourceType, context, lowerNode);
        var forwardReferenceSearchParamId = context.LeafContext.SearchParamId(chain.ReferenceSearchParameter);
        var forwardInnerResourceTypeId = context.LeafContext.ResourceTypeId(targetResourceType);
        var forwardOutputResourceTypeIds = chain.ResourceTypes switch
        {
            { Length: > 0 } referencing => referencing.Select(context.LeafContext.ResourceTypeId).ToList(),
            _ => throw new NotSupportedException(EmptyOutputSideMessage("Forward", "referencing", "SearchKeyBinder.BindForward")),
        };

        return context.Graph.Add(new CteDefinition.ChainJoin(forwardInnerMatch, forwardReferenceSearchParamId, forwardInnerResourceTypeId, forwardOutputResourceTypeIds, ChainDirection.Forward));
    }

    /// <summary>The refusal for a chain whose <em>output</em> side named no resource type — a malformation
    /// rather than the ambiguity the must-be-single sides guard against.</summary>
    private static string EmptyOutputSideMessage(string direction, string side, string binderMethod)
        => $"{direction} chain's {side} side resolved to 0 resource types -- a chain join filters its output rows to " +
           "those types by interpolating an OR of type-id equalities into its WHERE clause, so an empty list emits " +
           "no filter text at all and the statement does not parse. The real binder never produces this shape " +
           $"({binderMethod}), so this guard covers IR built directly against the compiler API.";
}
