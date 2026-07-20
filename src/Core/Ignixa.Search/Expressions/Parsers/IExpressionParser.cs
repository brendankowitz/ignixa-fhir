// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>Parses a raw search key/value pair (and <c>_include</c>/<c>_revinclude</c>) into a search <see cref="Expression"/>.</summary>
public interface IExpressionParser
{
    Expression Parse(string[] resourceTypes, string key, string value);

    IncludeExpression ParseInclude(string[] resourceTypes, string includeValue, bool isReversed, bool iterate);
}
