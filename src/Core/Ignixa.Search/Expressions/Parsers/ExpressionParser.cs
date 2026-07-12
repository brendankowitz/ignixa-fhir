// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers.Binding;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Provides mechanism to parse the search expression.
/// </summary>
public class ExpressionParser : IExpressionParser
{
    private readonly SearchKeyBinder _keyBinder;
    private readonly ISearchParameterExpressionParser _valueParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpressionParser"/> class.
    /// </summary>
    /// <param name="searchParameterDefinitionManagerResolver">The search parameter definition manager.</param>
    /// <param name="searchParameterExpressionParser">The parser used to parse the search value into a search expression.</param>
    /// <param name="schemaProvider">FHIR Schema Provider</param>
    public ExpressionParser(
        ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver
            searchParameterDefinitionManagerResolver,
        ISearchParameterExpressionParser searchParameterExpressionParser,
        IFhirSchemaProvider schemaProvider)
    {
        EnsureArg.IsNotNull(
            searchParameterDefinitionManagerResolver,
            nameof(searchParameterDefinitionManagerResolver));
        EnsureArg.IsNotNull(
            searchParameterExpressionParser,
            nameof(searchParameterExpressionParser));
        EnsureArg.IsNotNull(schemaProvider, nameof(schemaProvider));

        _keyBinder = new SearchKeyBinder(
            searchParameterDefinitionManagerResolver(),
            schemaProvider);
        _valueParser = searchParameterExpressionParser;
    }

    /// <summary>
    /// Parses the input into a corresponding search expression.
    /// </summary>
    /// <param name="resourceTypes">The resource type.</param>
    /// <param name="key">The query key.</param>
    /// <param name="value">The query value.</param>
    /// <returns>An instance of search expression representing the search.</returns>
    public Expression Parse(string[] resourceTypes, string key, string value)
    {
        EnsureArg.HasItems(resourceTypes, nameof(resourceTypes));
        EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));
        EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

        if (key.Equals("_not-referenced", StringComparison.OrdinalIgnoreCase))
        {
            NotReferencedKeySyntax syntax =
                SearchKeySyntaxParser.ParseNotReferenced(value);
            return SearchExpressionBinder.BindNotReferenced(
                _keyBinder.BindNotReferenced(syntax));
        }

        SearchKeySyntax keySyntax = SearchKeySyntaxParser.ParseParameter(key);
        BoundSearchKey bound = _keyBinder.Bind(resourceTypes, keySyntax);
        return SearchExpressionBinder.BindKey(
            bound,
            parameter => _valueParser.Parse(
                parameter.SearchParameter,
                parameter.Modifier,
                value));
    }

    public IncludeExpression ParseInclude(
        string[] resourceTypes,
        string includeValue,
        bool isReversed,
        bool iterate)
    {
        EnsureArg.HasItems(resourceTypes, nameof(resourceTypes));
        EnsureArg.IsNotNullOrWhiteSpace(includeValue, nameof(includeValue));

        if (!includeValue.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidSearchOperationException(
                isReversed
                    ? Resources.RevIncludeMissingType
                    : Resources.IncludeMissingType);
        }

        if (includeValue.EndsWith(':'))
        {
            string[] parts = includeValue.Split(':');
            throw new InvalidSearchOperationException(string.Format(
                Resources.IncludeInvalidTargetResourceType,
                isReversed ? "_revinclude" : "_include",
                parts[0],
                parts.Length > 1 ? parts[1] : string.Empty,
                "<empty>"));
        }

        IncludeKeySyntax syntax = SearchKeySyntaxParser.ParseInclude(includeValue);
        BoundIncludeKey bound = _keyBinder.BindInclude(
            resourceTypes,
            syntax,
            isReversed,
            iterate);
        return SearchExpressionBinder.BindInclude(
            resourceTypes,
            bound,
            isReversed,
            iterate);
    }
}
