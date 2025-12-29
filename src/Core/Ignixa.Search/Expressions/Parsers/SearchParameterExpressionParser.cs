// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using Ignixa.Search.Exceptions;
using Ignixa.Specification;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// A builder used to build expression from the search value.
/// </summary>
public class SearchParameterExpressionParser : ISearchParameterExpressionParser
{
    private static readonly Tuple<string, SearchComparator>[] SearchParamComparators = Enum.GetValues(typeof(SearchComparator))
        .Cast<SearchComparator>()
        .Select(e => Tuple.Create(e.GetLiteral(), e)).ToArray();

    private readonly Dictionary<SearchParamType, Func<string, ISearchValue>> _parserDictionary;

    public SearchParameterExpressionParser(IReferenceSearchValueParser referenceSearchValueParser, IFhirSchemaProvider fhirSchemaProvider)
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
                // separateCanonicalComponents=false stores the full URI including version and fragment
                // in the Uri column. This ensures exact matching works correctly.
                // Note: Canonical version/fragment search requires schema migration to add
                // separate Version/Fragment columns. Until then, use full URI matching.
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
            // We have to handle :missing modifier specially because if :missing modifier is specified,
            // then the value is a boolean string indicating whether the parameter is missing or not instead of
            // the search value type associated with the search parameter.
            if (!bool.TryParse(value, out bool isMissing))
                // An invalid value was specified.
                throw new InvalidSearchOperationException(Resources.InvalidValueTypeForMissingModifier);

            return Expression.MissingSearchParameter(searchParameter, isMissing);
        }

        if (modifier?.SearchModifierCode == SearchModifierCode.Text)
        {
            // We have to handle :text modifier specially because if :text modifier is supplied for token search param,
            // then we want to search the display text using the specified text, and therefore
            // we don't want to actually parse the specified text into token.
            if (searchParameter.Type != SearchParamType.Token)
                throw new InvalidSearchOperationException(
                    string.Format(CultureInfo.InvariantCulture, Resources.ModifierNotSupported, modifier, searchParameter.Code));

            outputExpression = Expression.StartsWith(FieldName.TokenText, null, value, true);
        }
        else if (modifier?.SearchModifierCode == SearchModifierCode.OfType)
        {
            // The :of-type modifier is used to search identifiers by their type.
            // Format: identifier:of-type=system|code|value
            // Where system is the Identifier.type.coding.system
            //       code is the Identifier.type.coding.code
            //       value is the Identifier.value
            if (searchParameter.Type != SearchParamType.Token)
                throw new InvalidSearchOperationException(
                    string.Format(CultureInfo.InvariantCulture, Resources.ModifierNotSupported, modifier, searchParameter.Code));

            outputExpression = BuildOfTypeExpression(searchParameter, value);
        }
        else
        {
            // Build the expression for based on the search value.
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
                        // Find the corresponding search parameter info.
                        SearchParameterInfo componentSearchParameter = searchParameter.Component[componentIndex].ResolvedSearchParameter;

                        if (componentSearchParameter == null)
                        {
                            // Component not resolved - this indicates a search parameter definition issue
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

                        // For composite components, infer the actual type from the value since some FHIR
                        // search parameter definitions have mismatched component definitions (e.g., DocumentReference
                        // "relationship" parameter has swapped definitions).
                        var effectiveSearchParameter = componentSearchParameter;
                        var inferredType = InferSearchParamTypeFromValue(componentValue);
                        if (inferredType.HasValue && inferredType != componentSearchParameter.Type)
                        {
                            // Create a synthetic search parameter info with the inferred type
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

    private Expression Build(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        int? componentIndex,
        string value)
    {
        ReadOnlySpan<char> valueSpan = value.AsSpan();

        // By default, the comparator is equal.
        SearchComparator comparator = SearchComparator.Eq;

        if (searchParameter.Type == SearchParamType.Date ||
            searchParameter.Type == SearchParamType.Number ||
            searchParameter.Type == SearchParamType.Quantity)
        {
            // If the search parameter type supports comparator, parse the comparator (if present).
            Tuple<string, SearchComparator> matchedComparator = SearchParamComparators.FirstOrDefault(
                s => value.StartsWith(s.Item1, StringComparison.Ordinal));

            if (matchedComparator != null)
            {
                comparator = matchedComparator.Item2;
                valueSpan = valueSpan.Slice(matchedComparator.Item1.Length);
            }
        }

        // Parse the value.
        Func<string, ISearchValue> parser = _parserDictionary[Enum.Parse<SearchParamType>(searchParameter.Type.ToString())];

        // Build the expression.
        var helper = new SearchValueExpressionBuilderHelper();

        // If the value contains comma, then we need to convert it into in expression.
        // But in this case, the user cannot specify prefix.
        IReadOnlyList<string> parts = value.SplitByOrSeparator();

        if (parts.Count == 1)
        {
            // This is a single value expression.
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

            // This is a multiple value expression.
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

    /// <summary>
    /// Infers the search parameter type from the search value string.
    /// This is used for composite components where the component definition type
    /// may not match the actual value type due to FHIR spec inconsistencies.
    /// Returns null when the type cannot be confidently determined, allowing
    /// the component definition type to be used instead.
    /// </summary>

    private Expression BuildOfTypeExpression(SearchParameterInfo searchParameter, string value)
    {
        IReadOnlyList<string> parts = value.SplitByOrSeparator();
        var helper = new SearchValueExpressionBuilderHelper();

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

        // Reference values typically contain a "/" (e.g., "Patient/123", "DocumentReference/document1")
        // or are URLs starting with http/https
        if (value.Contains('/', StringComparison.Ordinal) && !value.Contains('|', StringComparison.Ordinal))
        {
            // Check if it looks like a relative reference (ResourceType/id)
            var parts = value.Split('/');
            if (parts.Length >= 2)
            {
                var potentialResourceType = parts[0];
                // FHIR resource types are PascalCase and start with uppercase
                if (potentialResourceType.Length > 0 &&
                    char.IsUpper(potentialResourceType[0]) &&
                    potentialResourceType.All(c => char.IsLetterOrDigit(c)))
                {
                    return SearchParamType.Reference;
                }
            }

        }

        // Token values with explicit system|code format are definitely tokens
        // Only infer Token for values that have a pipe (system|code format)
        // This avoids misclassifying quantities, numbers, and strings as tokens
        if (value.Contains('|', StringComparison.Ordinal))
        {
            return SearchParamType.Token;
        }

        // Unable to determine - return null to use the component definition type
        // This is the conservative approach: let the search parameter definition
        // determine the type rather than guessing based on value format
        return null;
    }
}
