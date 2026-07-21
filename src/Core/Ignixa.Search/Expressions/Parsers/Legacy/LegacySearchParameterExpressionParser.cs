// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

// See LegacyExpressionParser.cs for why this exists and how to use it as a rollback lever.

using System.Globalization;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Search.Exceptions;
using Ignixa.Specification;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization;

namespace Ignixa.Search.Expressions.Parsers.Legacy;

/// <summary>The pre-rewrite per-parameter value parser, frozen alongside <see cref="LegacyExpressionParser"/> as a rollback lever and parity-test oracle.</summary>
public sealed class LegacySearchParameterExpressionParser : ISearchParameterExpressionParser
{
    private static readonly Tuple<string, SearchComparator>[] SearchParamComparators = Enum.GetValues(typeof(SearchComparator))
        .Cast<SearchComparator>()
        .Select(e => Tuple.Create(e.GetLiteral(), e)).ToArray();

    private readonly Dictionary<SearchParamType, Func<string, ISearchValue>> _parserDictionary;

    public LegacySearchParameterExpressionParser(IReferenceSearchValueParser referenceSearchValueParser, IFhirSchemaProvider fhirSchemaProvider)
    {
        EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

        _parserDictionary = new (SearchParamType type, Func<string, ISearchValue> parser)[]
            {
                (SearchParamType.Date, DateTimeSearchValue.Parse),
                (SearchParamType.Number, NumberSearchValue.Parse),
                (SearchParamType.Quantity, QuantitySearchValue.Parse),
                (SearchParamType.Reference, referenceSearchValueParser.Parse),
                (SearchParamType.String, StringSearchValue.Parse),
                (SearchParamType.Token, TokenSearchValue.Parse),
                (SearchParamType.Uri, str => UriSearchValue.Parse(str, false, fhirSchemaProvider))
            }
            .ToDictionary(entry => entry.type, entry => CreateParserWithErrorHandling(entry.parser));
    }

    public Expression Parse(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
    {
        EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
        EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

        Expression outputExpression;

        if (modifier?.SearchModifierCode == SearchModifierCode.Missing)
        {
            if (!bool.TryParse(value, out bool isMissing))
                throw new InvalidSearchOperationException(Resources.InvalidValueTypeForMissingModifier);

            return Expression.MissingSearchParameter(searchParameter, isMissing);
        }

        if (modifier?.SearchModifierCode == SearchModifierCode.Text)
        {
            if (searchParameter.Type != SearchParamType.Token)
                throw new InvalidSearchOperationException(
                    string.Format(CultureInfo.InvariantCulture, Resources.ModifierNotSupported, modifier, searchParameter.Code));

            outputExpression = Expression.StartsWith(FieldName.TokenText, null, value, true);
        }
        else if (modifier?.SearchModifierCode == SearchModifierCode.OfType)
        {
            if (searchParameter.Type != SearchParamType.Token)
                throw new InvalidSearchOperationException(
                    string.Format(CultureInfo.InvariantCulture, Resources.ModifierNotSupported, modifier, searchParameter.Code));

            outputExpression = BuildOfTypeExpression(searchParameter, value);
        }
        else
        {
            if (searchParameter.Type == SearchParamType.Composite)
            {
                if (modifier != null)
                    throw new InvalidSearchOperationException(
                        string.Format(CultureInfo.InvariantCulture, Resources.ModifierNotSupported, modifier, searchParameter.Code));

                IReadOnlyList<string> orParts = value.SplitByOrSeparator();
                var orExpressions = new Expression[orParts.Count];
                for (int orIndex = 0; orIndex < orParts.Count; orIndex++)
                {
                    IReadOnlyList<string> compositeValueParts = orParts[orIndex].SplitByCompositeSeparator();

                    if (compositeValueParts.Count > searchParameter.Component.Count)
                        throw new InvalidSearchOperationException(
                            string.Format(CultureInfo.InvariantCulture, Resources.NumberOfCompositeComponentsExceeded, searchParameter.Code));

                    var compositeExpressions = new Expression[compositeValueParts.Count];

                    for (int componentIndex = 0; componentIndex < compositeValueParts.Count; componentIndex++)
                    {
                        SearchParameterInfo componentSearchParameter = searchParameter.Component[componentIndex].ResolvedSearchParameter;

                        if (componentSearchParameter == null)
                        {
                            throw new InvalidSearchOperationException(
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Composite search parameter '{0}' component {1} (definition: {2}) is not properly resolved. " +
                                    "This indicates the search parameter was not properly built during initialization.",
                                    searchParameter.Code,
                                    componentIndex,
                                    searchParameter.Component[componentIndex].DefinitionUrl?.ToString() ?? "unknown"));
                        }

                        string componentValue = compositeValueParts[componentIndex];

                        var effectiveSearchParameter = componentSearchParameter;
                        var inferredType = InferSearchParamTypeFromValue(componentValue);
                        if (inferredType.HasValue && inferredType != componentSearchParameter.Type)
                        {
                            effectiveSearchParameter = new SearchParameterInfo(
                                componentSearchParameter.Name,
                                componentSearchParameter.Code,
                                inferredType.Value,
                                componentSearchParameter.Url,
                                componentSearchParameter.Component,
                                componentSearchParameter.Expression,
                                componentSearchParameter.TargetResourceTypes,
                                componentSearchParameter.BaseResourceTypes,
                                componentSearchParameter.Description);
                        }

                        compositeExpressions[componentIndex] = Build(
                            effectiveSearchParameter,
                            null,
                            componentIndex,
                            componentValue);
                    }

                    orExpressions[orIndex] = Expression.And(compositeExpressions);
                }

                outputExpression = orExpressions.Length == 1 ? orExpressions[0] : Expression.Or(orExpressions);
            }
            else
            {
                outputExpression = Build(
                    searchParameter,
                    modifier,
                    null,
                    value);
            }
        }

        return Expression.SearchParameter(searchParameter, outputExpression);
    }

    public (Expression Expression, SyntaxNode ValueSyntax) ParseWithSyntax(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
        => throw new NotSupportedException(
            "The frozen legacy oracle parser does not produce syntax projections.");

    private Expression Build(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        int? componentIndex,
        string value)
    {
        ReadOnlySpan<char> valueSpan = value.AsSpan();

        SearchComparator comparator = SearchComparator.Eq;

        if (searchParameter.Type == SearchParamType.Date ||
            searchParameter.Type == SearchParamType.Number ||
            searchParameter.Type == SearchParamType.Quantity)
        {
            Tuple<string, SearchComparator> matchedComparator = SearchParamComparators.FirstOrDefault(
                s => value.StartsWith(s.Item1, StringComparison.Ordinal));

            if (matchedComparator != null)
            {
                comparator = matchedComparator.Item2;
                valueSpan = valueSpan.Slice(matchedComparator.Item1.Length);
            }
        }

        Func<string, ISearchValue> parser = _parserDictionary[Enum.Parse<SearchParamType>(searchParameter.Type.ToString())];

        var helper = new LegacySearchValueExpressionBuilderHelper();

        IReadOnlyList<string> parts = value.SplitByOrSeparator();

        if (parts.Count == 1)
        {
            ISearchValue searchValue = parser(valueSpan.ToString());
            searchValue = ApplyTargetTypeModifier(modifier, searchValue);

            return helper.Build(
                searchParameter.Code,
                modifier,
                comparator,
                componentIndex,
                searchValue);
        }
        else
        {
            if (comparator != SearchComparator.Eq) throw new InvalidSearchOperationException(Resources.SearchComparatorNotSupported);

            if (modifier?.SearchModifierCode == SearchModifierCode.Not)
            {
                Expression[] expressions = parts.Select(part =>
                {
                    ISearchValue searchValue = parser(part);

                    return helper.Build(
                        searchParameter.Code,
                        null,
                        comparator,
                        componentIndex,
                        searchValue);
                }).ToArray();

                return Expression.Not(Expression.Or(expressions));
            }
            else
            {
                Expression[] expressions = parts.Select(part =>
                {
                    ISearchValue searchValue = parser(part);
                    searchValue = ApplyTargetTypeModifier(modifier, searchValue);

                    return helper.Build(
                        searchParameter.Code,
                        modifier,
                        comparator,
                        componentIndex,
                        searchValue);
                }).ToArray();

                return Expression.Or(expressions);
            }
        }

        ISearchValue ApplyTargetTypeModifier(SearchModifier modifier, ISearchValue source)
        {
            var referenceSearchValue = source as ReferenceSearchValue;
            if (referenceSearchValue == null || modifier?.SearchModifierCode != SearchModifierCode.Type) return source;

            if (!string.IsNullOrEmpty(referenceSearchValue.ResourceType))
            {
                if (string.Equals(referenceSearchValue.ResourceType, modifier.ResourceType, StringComparison.OrdinalIgnoreCase)) return source;

                throw new InvalidSearchOperationException(
                    string.Format(Resources.ModifierNotSupported, modifier, searchParameter.Code));
            }

            try
            {
                return new ReferenceSearchValue(
                    referenceSearchValue.Kind,
                    referenceSearchValue.BaseUri,
                    modifier.ResourceType,
                    referenceSearchValue.ResourceId);
            }
            catch (ArgumentException)
            {
                throw new InvalidSearchOperationException(
                    string.Format(Resources.ModifierNotSupported, modifier, searchParameter.Code));
            }
        }
    }

    private static Func<string, ISearchValue> CreateParserWithErrorHandling(Func<string, ISearchValue> parser)
    {
        return input =>
        {
            try
            {
                return parser(input);
            }
            catch (FormatException e)
            {
                throw new BadSearchRequestException(e.Message);
            }
            catch (OverflowException e)
            {
                throw new BadSearchRequestException(e.Message);
            }
            catch (ArgumentException e)
            {
                throw new BadSearchRequestException(e.Message);
            }
        };
    }

    private Expression BuildOfTypeExpression(SearchParameterInfo searchParameter, string value)
    {
        IReadOnlyList<string> parts = value.SplitByOrSeparator();
        var helper = new LegacySearchValueExpressionBuilderHelper();

        if (parts.Count == 1)
        {
            var searchValue = OfTypeTokenSearchValue.Parse(value);
            return helper.Build(
                searchParameter.Code,
                null,
                SearchComparator.Eq,
                null,
                searchValue);
        }
        else
        {
            var expressions = parts.Select(part =>
            {
                var searchValue = OfTypeTokenSearchValue.Parse(part);
                return helper.Build(
                    searchParameter.Code,
                    null,
                    SearchComparator.Eq,
                    null,
                    searchValue);
            }).ToArray();

            return Expression.Or(expressions);
        }
    }

    private static SearchParamType? InferSearchParamTypeFromValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (value.Contains('/', StringComparison.Ordinal) && !value.Contains('|', StringComparison.Ordinal))
        {
            var parts = value.Split('/');
            if (parts.Length >= 2)
            {
                var potentialResourceType = parts[0];
                if (potentialResourceType.Length > 0 &&
                    char.IsUpper(potentialResourceType[0]) &&
                    potentialResourceType.All(c => char.IsLetterOrDigit(c)))
                {
                    return SearchParamType.Reference;
                }
            }
        }

        if (value.Contains('|', StringComparison.Ordinal))
        {
            return SearchParamType.Token;
        }

        return null;
    }
}
