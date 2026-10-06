// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Builds expressions from search values.
/// </summary>
public class SearchParameterExpressionParser : ISearchParameterExpressionParser
{
    private readonly SearchExpressionBinder _binder;

    public SearchParameterExpressionParser(
        IReferenceSearchValueParser referenceSearchValueParser,
        IFhirSchemaProvider fhirSchemaProvider)
    {
        EnsureArg.IsNotNull(
            referenceSearchValueParser,
            nameof(referenceSearchValueParser));
        EnsureArg.IsNotNull(fhirSchemaProvider, nameof(fhirSchemaProvider));

        _binder = new SearchExpressionBinder(
            new SearchAtomicValueParser(
                referenceSearchValueParser,
                fhirSchemaProvider));
    }

    public Expression Parse(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
    {
        (SearchValueSyntax _, Expression expression) = ParseCore(searchParameter, modifier, value);
        return expression;
    }

    public (Expression Expression, SyntaxNode ValueSyntax) ParseWithSyntax(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
    {
        (SearchValueSyntax syntax, Expression expression) = ParseCore(searchParameter, modifier, value);
        return (expression, SyntaxProjector.Project(syntax));
    }

    private (SearchValueSyntax Syntax, Expression Expression) ParseCore(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
    {
        EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
        EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

        // Semantic parameters are bound here, at the earliest point that has the resolved
        // SearchParameterInfo, rather than falling into SearchValueSyntaxParser.Parse below: that scanner
        // only sees the raw SearchParamType (Special) and would split the value on unescaped commas like
        // any other scalar, destroying a query such as "chest pain, nausea" into two ORed alternatives
        // instead of one verbatim embedding request (Review Focus #1 in the slice-1 plan).
        if (searchParameter.IsSemantic)
        {
            return ParseSemantic(searchParameter, modifier, value);
        }

        SearchValueSyntax syntax = SearchValueSyntaxParser.Parse(
            searchParameter.Type,
            modifier,
            value);

        return (syntax, _binder.BindValue(searchParameter, modifier, syntax));
    }

    private static (SearchValueSyntax Syntax, Expression Expression) ParseSemantic(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
    {
        if (!searchParameter.IsSupported)
        {
            // Covers both an invalid vector-search-config extension (SearchParameterInfo.IsSupported set
            // false at definition load) and the VectorSearchEnabled=false feature gate
            // (CompositeSearchParameterDefinitionManager sets the same flag for the same reason): either
            // way the parameter must behave as unsupported here, which is what drives
            // SearchOptionsBuilder's existing lenient-ignore / strict-reject handling for this exception
            // type.
            throw new SearchParameterNotSupportedException(searchParameter.Url);
        }

        // No modifier is defined for semantic search (:text, :missing, :exact, etc. all presuppose a
        // typed, filterable value this parameter does not have).
        if (modifier is not null)
        {
            throw new InvalidSearchOperationException(string.Format(
                CultureInfo.InvariantCulture,
                Resources.SemanticSearchModifierNotSupported,
                modifier,
                searchParameter.Code));
        }

        // Trim only at the ends -- no comma splitting, no escape processing beyond the URL decoding
        // already done upstream. No empty-after-trim check is needed: ParseCore's own
        // EnsureArg.IsNotNullOrWhiteSpace above already guarantees value contains at least one
        // non-whitespace character, so Trim() can never reduce it to an empty string here.
        string queryText = value.Trim();

        var syntax = new AtomicValueSyntax(queryText, SearchComparator.Eq)
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, queryText.Length),
        };

        return (syntax, new VectorSearchExpression(searchParameter, queryText));
    }
}
