// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions.Parsers;

public interface IExpressionParser
{
    Expression Parse(string[] resourceTypes, string key, string value);

    IncludeExpression ParseInclude(string[] resourceTypes, string includeValue, bool isReversed, bool iterate);
}
