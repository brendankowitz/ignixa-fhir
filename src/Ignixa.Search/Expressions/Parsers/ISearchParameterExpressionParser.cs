// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers;

public interface ISearchParameterExpressionParser
{
    Expression Parse(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value);
}
