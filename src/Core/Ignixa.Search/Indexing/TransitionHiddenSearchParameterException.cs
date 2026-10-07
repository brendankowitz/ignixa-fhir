// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Thrown when a parameter is hidden while a lifecycle transition changes its definition.
/// </summary>
public sealed class TransitionHiddenSearchParameterException(SearchParameterInfo searchParameter)
    : SearchParameterNotSupportedException(searchParameter.Code)
{
    public SearchParameterInfo SearchParameter { get; } = searchParameter;
}
