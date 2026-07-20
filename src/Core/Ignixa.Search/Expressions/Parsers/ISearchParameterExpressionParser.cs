// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>Parses a single search parameter's raw value into an <see cref="Expression"/>, given the resolved parameter and modifier.</summary>
public interface ISearchParameterExpressionParser
{
    Expression Parse(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value);
}
