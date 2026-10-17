// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;
using Ignixa.Search.Models;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// Collects, in pre-order, what one parameter's parsed expression binds: every definition on the types it applies to (a
/// chain's reference on its own types, then its inner expression on the chain's targets, or on its source types for
/// <c>_has</c>), the modifiers and comparison prefixes of its predicates, and whether <c>:missing=true</c> makes an absent
/// value match. For one parameter; not thread-safe.
/// </summary>
internal sealed class PlanCollector(ParameterPopulation population)
{
    public List<ParameterBinding> Bindings { get; } = [];

    public HashSet<string> Modifiers { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Prefixes { get; } = new(StringComparer.Ordinal);

    /// <summary>True when the parameter is <c>:missing=true</c>, which matches resources without a value.</summary>
    public bool MissingMatches { get; private set; }

    public void Walk(Expression expression, IReadOnlyList<string> types)
    {
        switch (expression)
        {
            case ChainedExpression chain:
                Bindings.AddRange(chain.ResourceTypes.Select(type => SearchPlanner.Binding(chain.Reversed ? "reverse-chain" : "chain", type, chain.ReferenceSearchParameter, chain.TargetResourceTypes, population)));
                Walk(chain.Expression, chain.Reversed ? chain.ResourceTypes : chain.TargetResourceTypes);
                break;
            case SearchParameterExpression parameter:
                AddFilters(parameter.Parameter, types);
                Walk(parameter.Expression, types);
                break;
            case MissingSearchParameterExpression missing:
                AddFilters(missing.Parameter, types);
                Modifiers.Add("missing");
                MissingMatches |= missing.IsMissing;
                break;
            case SearchParameterPredicateExpression predicate:
                Prefixes.Add(SearchPlanner.Literal(predicate.Comparator));
                if (predicate.Modifier is { } modifier)
                {
                    Modifiers.Add(SearchPlanner.Literal(modifier.SearchModifierCode));
                }

                break;
            case NotExpression not:
                Walk(not.Expression, types);
                break;
            case MultiaryExpression multiary:
                foreach (Expression part in multiary.Expressions)
                {
                    Walk(part, types);
                }

                break;
            case UnionExpression union:
                foreach (Expression part in union.Expressions)
                {
                    Walk(part, types);
                }

                break;
        }
    }

    private void AddFilters(SearchParameterInfo parameter, IReadOnlyList<string> types)
    {
        foreach (string type in types)
        {
            // A predicate under the parameter repeats it (an OR of values); bind each definition once per type.
            if (!Bindings.Any(binding => binding.Role == "filter" && binding.ResourceType == type && binding.Url == parameter.Url.OriginalString))
            {
                Bindings.Add(SearchPlanner.Binding("filter", type, parameter, targets: null, population));
            }
        }
    }
}
