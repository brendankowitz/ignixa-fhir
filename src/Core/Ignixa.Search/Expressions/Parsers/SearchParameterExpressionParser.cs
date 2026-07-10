// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

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
        EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
        EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

        SearchValueSyntax syntax = SearchValueGrammar.Parse(
            searchParameter.Type,
            modifier,
            value);

        return _binder.BindValue(searchParameter, modifier, syntax);
    }
}
