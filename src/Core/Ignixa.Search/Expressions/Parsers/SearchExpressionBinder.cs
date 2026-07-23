// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Diagnostics;
using System.Globalization;
using Ignixa.Search.Expressions.Parsers.Binding;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Builds the typed predicate <see cref="Expression"/> tree from a <see cref="Binding.BoundSearchKey"/>
/// and scanned <see cref="Syntax.SearchValueSyntax"/> — the final stage of the scan → bind → build
/// pipeline. Produces <see cref="SearchParameterPredicateExpression"/> leaves and composites, and wraps
/// chains, includes, and <c>_not-referenced</c>.
/// </summary>
internal sealed class SearchExpressionBinder(SearchAtomicValueParser atomicValueParser)
{
    internal static Expression BindKey(
        BoundSearchKey key,
        Func<BoundParameterKey, Expression> bindParameter) =>
        key switch
        {
            BoundParameterKey parameter => bindParameter(parameter),
            BoundChainKey chain => Expression.Chained(
                chain.ResourceTypes.ToArray(),
                chain.ReferenceSearchParameter,
                chain.TargetResourceTypes.ToArray(),
                chain.Reversed,
                BindKey(chain.Next, bindParameter)),
            _ => throw new UnreachableException(),
        };

    internal static IncludeExpression BindInclude(
        string[] resourceTypes,
        BoundIncludeKey include,
        bool isReversed,
        bool iterate) =>
        new(
            resourceTypes,
            include.ReferenceSearchParameter,
            include.SourceResourceType,
            include.TargetResourceType,
            include.ReferencedTypes,
            include.Wildcard,
            isReversed,
            iterate);

    internal static NotReferencedExpression BindNotReferenced(
        BoundNotReferencedKey notReferenced) =>
        Expression.NotReferenced(
            notReferenced.SourceResourceType,
            notReferenced.ReferencePath);

    internal Expression BindValue(
        SearchParameterInfo searchParameter,
        SearchModifier? modifier,
        SearchValueSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(searchParameter);
        ArgumentNullException.ThrowIfNull(syntax);

        if (syntax is MissingValueSyntax missing)
        {
            return Expression.MissingSearchParameter(
                searchParameter,
                missing.IsMissing);
        }

        if (modifier?.SearchModifierCode == SearchModifierCode.Text)
        {
            if (searchParameter.Type != SearchParamType.Token ||
                syntax is not AtomicValueSyntax text)
            {
                throw new InvalidSearchOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    Resources.ModifierNotSupported,
                    modifier,
                    searchParameter.Code));
            }

            return Expression.SearchParameter(
                searchParameter,
                Expression.StartsWith(FieldName.TokenText, null, text.RawText, true));
        }

        Expression body = syntax switch
        {
            AtomicValueSyntax atomic => BindAtomic(
                searchParameter,
                modifier,
                componentIndex: null,
                atomic),
            AlternativesValueSyntax alternatives => BindAlternatives(
                searchParameter,
                modifier,
                alternatives),
            CompositeValueSyntax composite => BindComposite(
                searchParameter,
                modifier,
                composite),
            OfTypeValueSyntax ofType => BindOfType(
                searchParameter,
                ofType),
            _ => throw new UnreachableException(),
        };

        return Expression.SearchParameter(searchParameter, body);
    }

    private Expression BindAlternatives(
        SearchParameterInfo searchParameter,
        SearchModifier? modifier,
        AlternativesValueSyntax syntax)
    {
        foreach (SearchValueSyntax item in syntax.Items)
        {
            if (item is AtomicValueSyntax { Comparator: not SearchComparator.Eq })
            {
                throw new InvalidSearchOperationException(
                    Resources.SearchComparatorNotSupported);
            }
        }

        bool isNot = modifier?.SearchModifierCode == SearchModifierCode.Not;
        SearchModifier? itemModifier = isNot ? null : modifier;
        var items = new Expression[syntax.Items.Length];
        for (var index = 0; index < syntax.Items.Length; index++)
        {
            items[index] = syntax.Items[index] switch
            {
                AtomicValueSyntax atomic => BindAtomic(
                    searchParameter,
                    itemModifier,
                    componentIndex: null,
                    atomic),
                CompositeValueSyntax composite => BindComposite(
                    searchParameter,
                    modifier,
                    composite),
                OfTypeValueSyntax ofType => BindOfType(
                    searchParameter,
                    ofType),
                _ => throw new UnreachableException(),
            };
        }

        Expression alternatives = Expression.Or(items);

        return isNot
            ? Expression.Not(alternatives)
            : alternatives;
    }

    private Expression BindOfType(
        SearchParameterInfo searchParameter,
        OfTypeValueSyntax syntax)
    {
        if (searchParameter.Type != SearchParamType.Token)
        {
            throw new InvalidSearchOperationException(string.Format(
                CultureInfo.InvariantCulture,
                Resources.ModifierNotSupported,
                SearchModifierCode.OfType.ToString(),
                searchParameter.Code));
        }

        string raw = string.Join(
            '|',
            syntax.TypeSystem,
            syntax.TypeCode,
            syntax.IdentifierValue);
        OfTypeTokenSearchValue value = atomicValueParser.ParseOfType(raw);

        return new SearchValueExpressionBuilderHelper().Build(
            searchParameter.Code,
            modifier: null,
            SearchComparator.Eq,
            componentIndex: null,
            value);
    }

    private Expression BindComposite(
        SearchParameterInfo searchParameter,
        SearchModifier? modifier,
        CompositeValueSyntax syntax)
    {
        if (modifier is not null)
        {
            throw new InvalidSearchOperationException(string.Format(
                CultureInfo.InvariantCulture,
                Resources.ModifierNotSupported,
                modifier,
                searchParameter.Code));
        }

        if (syntax.Components.Length > searchParameter.Component.Count)
        {
            throw new InvalidSearchOperationException(string.Format(
                CultureInfo.InvariantCulture,
                Resources.NumberOfCompositeComponentsExceeded,
                searchParameter.Code));
        }

        var expressions = new Expression[syntax.Components.Length];
        for (var index = 0; index < syntax.Components.Length; index++)
        {
            SearchParameterComponentInfo component = searchParameter.Component[index];
            SearchParameterInfo resolved = component.ResolvedSearchParameter
                ?? throw new InvalidSearchOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    Resources.CompositeSearchParameterComponentNotResolved,
                    searchParameter.Code,
                    index,
                    component.DefinitionUrl?.ToString() ?? "unknown"));
            SearchParameterInfo effective = InferEffectiveParameter(
                resolved,
                syntax.Components[index].RawText);
            AtomicValueSyntax componentSyntax = NormalizeCompositeComparator(
                effective.Type,
                syntax.Components[index]);
            expressions[index] = new CompositeComponentExpression(
                effective,
                index,
                BindAtomic(
                    effective,
                    modifier: null,
                    index,
                    componentSyntax))
            {
                Span = componentSyntax.Span,
            };
        }

        return Expression.And(expressions);
    }

    private static AtomicValueSyntax NormalizeCompositeComparator(
        SearchParamType componentType,
        AtomicValueSyntax syntax)
    {
        if (syntax.Comparator == SearchComparator.Eq ||
            componentType is
                SearchParamType.Date or
                SearchParamType.Number or
                SearchParamType.Quantity)
        {
            return syntax;
        }

        return new AtomicValueSyntax(
            $"{syntax.Comparator.GetLiteral()}{syntax.RawText}",
            SearchComparator.Eq) { Span = syntax.Span };
    }

    private static SearchParameterInfo InferEffectiveParameter(
        SearchParameterInfo component,
        string value)
    {
        // A quantity, number, or date component owns the '|' and '/' characters within its own value
        // grammar -- a quantity value is [prefix]number|system|code -- so the "any '|' means token, any
        // '/' means reference" heuristic below must not fire for it. Without this guard a value-quantity
        // component carrying a full system|code (e.g. gt38|http://unitsofmeasure.org|Cel) is misread as a
        // token with two separators and the whole search is rejected. The same three ordered types are
        // the ones NormalizeCompositeComparator already treats specially, for the same reason.
        if (component.Type is SearchParamType.Quantity or SearchParamType.Number or SearchParamType.Date)
        {
            return component;
        }

        SearchParamType? inferred = InferSearchParamTypeFromValue(value);
        if (inferred is null || inferred == component.Type)
        {
            return component;
        }

        return new SearchParameterInfo(
            component.Name,
            component.Code,
            inferred.Value,
            component.Url,
            component.Component,
            component.Expression,
            component.TargetResourceTypes,
            component.BaseResourceTypes,
            component.Description);
    }

    private static SearchParamType? InferSearchParamTypeFromValue(string value)
    {
        if (value.Contains('/', StringComparison.Ordinal) &&
            !value.Contains('|', StringComparison.Ordinal))
        {
            string[] parts = value.Split('/');
            if (parts.Length >= 2 &&
                parts[0].Length > 0 &&
                char.IsUpper(parts[0][0]) &&
                parts[0].All(char.IsLetterOrDigit))
            {
                return SearchParamType.Reference;
            }
        }

        return value.Contains('|', StringComparison.Ordinal)
            ? SearchParamType.Token
            : null;
    }

    private Expression BindAtomic(
        SearchParameterInfo searchParameter,
        SearchModifier? modifier,
        int? componentIndex,
        AtomicValueSyntax syntax)
    {
        ISearchValue value = atomicValueParser.Parse(
            searchParameter.Type,
            syntax.RawText);
        value = ApplyReferenceTarget(searchParameter, modifier, value);

        // Validate the modifier/comparator against the value type here, at parse time, by running the
        // value through the same builder the SQL backend uses and discarding the result. An unsupported
        // combination (e.g. a comparator on a string, ':exact' on a date) throws
        // InvalidSearchOperationException now, inside SearchOptionsBuilder's parse-time catch, so the
        // parameter is gracefully ignored -- rather than surfacing later as a hard failure during lowering.
        _ = new SearchValueExpressionBuilderHelper().Build(searchParameter.Code, modifier, syntax.Comparator, componentIndex, value);

        return new SearchPredicateExpressionBuilder().Build(
            searchParameter, modifier, syntax.Comparator, value, syntax.Span);
    }

    private static ISearchValue ApplyReferenceTarget(
        SearchParameterInfo searchParameter,
        SearchModifier? modifier,
        ISearchValue value)
    {
        if (value is not ReferenceSearchValue reference ||
            modifier?.SearchModifierCode != SearchModifierCode.Type)
        {
            return value;
        }

        if (!string.IsNullOrEmpty(reference.ResourceType))
        {
            if (reference.ResourceType.Equals(
                    modifier.ResourceType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return reference;
            }

            throw new InvalidSearchOperationException(
                string.Format(Resources.ModifierNotSupported, modifier, searchParameter.Code));
        }

        try
        {
            return new ReferenceSearchValue(
                reference.Kind,
                reference.BaseUri,
                modifier.ResourceType,
                reference.ResourceId);
        }
        catch (ArgumentException)
        {
            throw new InvalidSearchOperationException(
                string.Format(Resources.ModifierNotSupported, modifier, searchParameter.Code));
        }
    }
}
