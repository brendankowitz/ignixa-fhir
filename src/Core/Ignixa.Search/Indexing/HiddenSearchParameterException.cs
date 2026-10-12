// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Thrown when binding resolves a supported parameter this request cannot see: its index is incomplete and the
/// request did not opt in to partial-index search, or a lifecycle transition is changing its definition, which
/// hides it even from requests that opted in.
/// </summary>
public sealed class HiddenSearchParameterException(SearchParameterInfo searchParameter)
    : SearchParameterNotSupportedException(searchParameter.Code)
{
    public SearchParameterInfo SearchParameter { get; } = searchParameter;

    /// <summary>
    /// Describes why <paramref name="parameter"/> is hidden, as a predicate such as <c>is pending reindex</c>.
    /// </summary>
    public static string DescribeHiddenState(SearchParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return parameter.IsHiddenByTransition ? "is being redefined" : "is pending reindex";
    }
}
