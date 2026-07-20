// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using EnsureThat;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Builds a <see cref="SearchParameterPredicateExpression"/> from the same typed
/// <see cref="ISearchValue"/> the parser already constructs during parsing — the sibling of
/// <see cref="SearchValueExpressionBuilderHelper"/>, which instead flattens that typed value into the
/// old field-level tree.
/// </summary>
internal sealed class SearchPredicateExpressionBuilder
{
    public SearchParameterPredicateExpression Build(
        SearchParameterInfo parameter,
        SearchModifier? modifier,
        SearchComparator comparator,
        ISearchValue value,
        SourceSpan? span = null)
    {
        EnsureArg.IsNotNull(parameter, nameof(parameter));
        EnsureArg.IsNotNull(value, nameof(value));

        return new SearchParameterPredicateExpression(parameter, comparator, modifier, value) { Span = span };
    }
}
