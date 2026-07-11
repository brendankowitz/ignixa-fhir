// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

// Frozen snapshot of Ignixa.Search.Expressions.Parsers.ExpressionParser as it existed on `main`
// before PR #332 (the handwritten-scanner search parser rewrite). See LegacyStringExtensions.cs for
// why this exists. Implements the SAME IExpressionParser interface as the current parser (that
// interface was not touched by PR #332), so this class and the current ExpressionParser can be
// constructed against identical ISearchParameterDefinitionManager/IFhirSchemaProvider fixtures and
// run side-by-side over the same input corpus - see SearchParserOldVsNewParityTests.

using System.Globalization;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Search;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers.Legacy;

internal sealed class LegacyExpressionParser : IExpressionParser
{
    private const char SearchSplitChar = ':';
    private const char ChainParameter = '.';
    internal const string ReverseChainParameter = "_has:";

    private static readonly Dictionary<string, SearchModifierCode> SearchParamModifierMapping = Enum.GetNames(typeof(SearchModifierCode))
        .Select(e => (SearchModifierCode)Enum.Parse(typeof(SearchModifierCode), e))
        .ToDictionary(
            e => e.GetLiteral(),
            e => e,
            StringComparer.Ordinal);

    private static readonly System.Text.RegularExpressions.Regex ReferencePathRegex =
        new(@"^[a-zA-Z][a-zA-Z0-9_-]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly IFhirSchemaProvider _schemaProvider;

    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
    private readonly ISearchParameterExpressionParser _searchParameterExpressionParser;

    public LegacyExpressionParser(
        ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
        ISearchParameterExpressionParser searchParameterExpressionParser,
        IFhirSchemaProvider schemaProvider)
    {
        EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
        EnsureArg.IsNotNull(searchParameterExpressionParser, nameof(searchParameterExpressionParser));
        EnsureArg.IsNotNull(schemaProvider, nameof(schemaProvider));

        _searchParameterDefinitionManager = searchParameterDefinitionManagerResolver();
        _searchParameterExpressionParser = searchParameterExpressionParser;
        _schemaProvider = schemaProvider;
    }

    public Expression Parse(string[] resourceTypes, string key, string value)
    {
        EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));
        EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

        return ParseImpl(resourceTypes, key.AsSpan(), value);
    }

    public IncludeExpression ParseInclude(string[] resourceTypes, string includeValue, bool isReversed, bool iterate)
    {
        ReadOnlySpan<char> valueSpan = includeValue.AsSpan();
        if (!TrySplit(SearchSplitChar, ref valueSpan, out ReadOnlySpan<char> originalType))
        {
            throw new InvalidSearchOperationException(isReversed ? Resources.RevIncludeMissingType : Resources.IncludeMissingType);
        }

        if (resourceTypes.Length == 1 && resourceTypes[0].Equals("DomainResource", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSearchOperationException(Resources.IncludeCannotBeAgainstBase);
        }

        SearchParameterInfo refSearchParameter;
        List<string> referencedTypes = null;
        bool wildCard = false;
        string targetType = null;

        if (valueSpan.Equals("*".AsSpan(), StringComparison.InvariantCultureIgnoreCase))
        {
            refSearchParameter = null;
            wildCard = true;
        }
        else
        {
            if (!TrySplit(SearchSplitChar, ref valueSpan, out ReadOnlySpan<char> searchParam))
                searchParam = valueSpan;
            else
                targetType = valueSpan.ToString();

            if (targetType != null && string.IsNullOrWhiteSpace(targetType))
            {
                throw new InvalidSearchOperationException(
                    string.Format(Resources.IncludeInvalidTargetResourceType,
                        isReversed ? "_revinclude" : "_include",
                        originalType.ToString(),
                        searchParam.ToString(),
                        "<empty>"));
            }

            if (!string.IsNullOrEmpty(targetType) && !_schemaProvider.ResourceTypeNames.Contains(targetType))
            {
                throw new InvalidSearchOperationException(
                    string.Format(Resources.IncludeInvalidTargetResourceType,
                        isReversed ? "_revinclude" : "_include",
                        originalType.ToString(),
                        searchParam.ToString(),
                        targetType));
            }

            refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(originalType.ToString(), searchParam.ToString());
        }

        if (isReversed && !iterate && targetType == null && resourceTypes.Length > 0)
        {
            targetType = resourceTypes[0];
        }

        if (wildCard)
        {
            referencedTypes = new List<string>();
            IEnumerable<SearchParameterInfo> searchParameters = resourceTypes.SelectMany(t => _searchParameterDefinitionManager.GetSearchParameters(t))
                .Where(p => p.Type == SearchParamType.Reference);

            foreach (SearchParameterInfo p in searchParameters)
            foreach (string t in p.TargetResourceTypes)
                if (!referencedTypes.Contains(t))
                    referencedTypes.Add(t);
        }

        return new IncludeExpression(resourceTypes, refSearchParameter, originalType.ToString(), targetType, referencedTypes, wildCard, isReversed, iterate);
    }

    private Expression ParseImpl(string[] resourceTypes, ReadOnlySpan<char> key, string value)
    {
        if (key.Equals("_not-referenced".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return ParseNotReferencedExpression(resourceTypes, value);
        }

        if (TryConsume(ReverseChainParameter.AsSpan(), ref key))
        {
            if (!TrySplit(SearchSplitChar, ref key, out ReadOnlySpan<char> type)) throw new InvalidSearchOperationException(Resources.ReverseChainMissingType);

            if (!TrySplit(SearchSplitChar, ref key, out ReadOnlySpan<char> refParam)) throw new InvalidSearchOperationException(Resources.ReverseChainMissingReference);

            string typeString = type.ToString();
            SearchParameterInfo refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(typeString, refParam.ToString());

            return ParseChainedExpression(new[] { typeString }, refSearchParameter, resourceTypes, key, value, true);
        }

        if (TrySplit(ChainParameter, ref key, out ReadOnlySpan<char> chainedInput))
        {
            string[] targetType = Array.Empty<string>();

            if (TrySplit(SearchSplitChar, ref chainedInput, out ReadOnlySpan<char> refParamName))
                targetType = new[] { chainedInput.ToString() };
            else
                refParamName = chainedInput;

            if (refParamName.IsEmpty) throw new SearchParameterNotSupportedException(resourceTypes[0], key.ToString());

            SearchParameterInfo refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(resourceTypes[0], refParamName.ToString());
            foreach (string resourceType in resourceTypes)
                if (refSearchParameter != _searchParameterDefinitionManager.GetSearchParameter(resourceType, refParamName.ToString()))
                    throw new BadSearchRequestException(string.Format(Resources.SearchParameterMustBeCommon, refParamName.ToString(), resourceTypes[0], resourceType));

            return ParseChainedExpression(resourceTypes, refSearchParameter, targetType, key, value, false);
        }

        ReadOnlySpan<char> modifier;

        if (TrySplit(SearchSplitChar, ref key, out ReadOnlySpan<char> paramName))
        {
            modifier = key;
        }
        else
        {
            paramName = key;
            modifier = ReadOnlySpan<char>.Empty;
        }

        SearchParameterInfo searchParameter = _searchParameterDefinitionManager.GetSearchParameter(resourceTypes[0], paramName.ToString());
        foreach (string resourceType in resourceTypes)
            if (searchParameter != _searchParameterDefinitionManager.GetSearchParameter(resourceType, paramName.ToString()))
                throw new BadSearchRequestException(string.Format(Resources.SearchParameterMustBeCommon, paramName.ToString(), resourceTypes[0], resourceType));

        return ParseSearchValueExpression(searchParameter, modifier.ToString(), value);
    }

    private Expression ParseChainedExpression(string[] resourceTypes, SearchParameterInfo searchParameter, string[] targetResourceTypes, ReadOnlySpan<char> remainingKey, string value, bool reversed)
    {
        if (searchParameter.Type != SearchParamType.Reference)
            throw new InvalidSearchOperationException(Resources.ChainedParameterMustBeReferenceSearchParamType);

        if (targetResourceTypes.Any())
            foreach (string targetResourceType in targetResourceTypes)
                if (!_schemaProvider.ResourceTypeNames.Contains(targetResourceType))
                    throw new InvalidSearchOperationException(string.Format(Resources.ResourceNotSupported, targetResourceType));

        IEnumerable<string> possibleTargetResourceTypes = targetResourceTypes.Any()
            ? targetResourceTypes.Intersect(searchParameter.TargetResourceTypes)
            : searchParameter.TargetResourceTypes;

        ChainedExpression chainedExpression = null;

        foreach (string possibleTargetResourceType in possibleTargetResourceTypes)
        {
            string[] wrappedTargetResourceType = new[] { possibleTargetResourceType };
            string[] multipleChainType = reversed ? resourceTypes : wrappedTargetResourceType;

            ChainedExpression expression;
            try
            {
                expression = Expression.Chained(
                    resourceTypes,
                    searchParameter,
                    wrappedTargetResourceType,
                    reversed,
                    ParseImpl(
                        multipleChainType,
                        remainingKey,
                        value));
            }
            catch (Exception ex) when (ex is SearchParameterNotSupportedException)
            {
                continue;
            }

            if (chainedExpression == null)
                chainedExpression = expression;
            else if (reversed)
                chainedExpression = Expression.Chained(
                    resourceTypes,
                    searchParameter,
                    chainedExpression.TargetResourceTypes.Append(possibleTargetResourceType).ToArray(),
                    reversed,
                    ParseImpl(
                        multipleChainType,
                        remainingKey,
                        value));
            else
                throw new InvalidSearchOperationException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ChainedParameterSpecifyType,
                        searchParameter.Name,
                        string.Join(Resources.OrDelimiter, searchParameter.TargetResourceTypes.Select(c => $"{searchParameter.Code}:{c}"))));
        }

        if (chainedExpression == null)
            throw new InvalidSearchOperationException(Resources.ChainedParameterNotSupported);

        return chainedExpression;
    }

    private Expression ParseSearchValueExpression(SearchParameterInfo searchParameter, string modifier, string value)
    {
        SearchModifier parsedModifier = ParseSearchParamModifier();
        return _searchParameterExpressionParser.Parse(searchParameter, parsedModifier, value);

        SearchModifier ParseSearchParamModifier()
        {
            if (string.IsNullOrEmpty(modifier)) return null;

            if (SearchParamModifierMapping.TryGetValue(modifier, out SearchModifierCode searchModifierCode)) return new SearchModifier(searchModifierCode);

            if (searchParameter.Type == SearchParamType.Reference && searchParameter.TargetResourceTypes.Contains(modifier, StringComparer.OrdinalIgnoreCase)) return new SearchModifier(SearchModifierCode.Type, modifier);

            throw new InvalidSearchOperationException(
                string.Format(Resources.ModifierNotSupported, modifier, searchParameter.Code));
        }
    }

    private static bool TrySplit(char splitChar, ref ReadOnlySpan<char> input, out ReadOnlySpan<char> captured)
    {
        int splitIndex = input.IndexOf(splitChar);
        if (splitIndex < 0)
        {
            captured = ReadOnlySpan<char>.Empty;
            return false;
        }

        captured = input.Slice(0, splitIndex);
        Advance(ref input, splitIndex + 1);
        return true;
    }

    private static bool TryConsume(ReadOnlySpan<char> toConsume, ref ReadOnlySpan<char> input)
    {
        if (input.StartsWith(toConsume))
        {
            Advance(ref input, toConsume.Length);
            return true;
        }

        return false;
    }

    private static void Advance(ref ReadOnlySpan<char> input, int to)
    {
        if (input.Length > to)
            input = input.Slice(to);
        else
            input = ReadOnlySpan<char>.Empty;
    }

    private Expression ParseNotReferencedExpression(string[] resourceTypes, string value)
    {
        var colonIndex = value.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex < 0)
        {
            throw new InvalidSearchOperationException(
                "_not-referenced value must be in format 'ResourceType:path', 'ResourceType:*', or '*:*'");
        }

        var sourceType = value.Substring(0, colonIndex);
        var path = value.Substring(colonIndex + 1);

        string sourceResourceType = sourceType == "*" ? null : sourceType;
        string referencePath = path == "*" ? null : path;

        if (sourceResourceType is not null && !_schemaProvider.ResourceTypeNames.Contains(sourceResourceType))
        {
            throw new InvalidSearchOperationException(
                $"Invalid resource type in _not-referenced: '{sourceResourceType}'");
        }

        if (referencePath is not null)
        {
            if (string.IsNullOrWhiteSpace(referencePath))
            {
                throw new InvalidSearchOperationException(
                    "_not-referenced path cannot be empty. Use '*' for wildcard.");
            }

            if (!ReferencePathRegex.IsMatch(referencePath))
            {
                throw new InvalidSearchOperationException(
                    $"Invalid reference path in _not-referenced: '{referencePath}'. Path must start with a letter and contain only alphanumeric characters, hyphens, and underscores.");
            }
        }

        return Expression.NotReferenced(sourceResourceType, referencePath);
    }
}
