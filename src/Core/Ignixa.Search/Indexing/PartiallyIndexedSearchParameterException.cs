// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Thrown when binding resolves a parameter whose index is not complete and the request did not opt in
/// to partial-index search.
/// </summary>
public sealed class PartiallyIndexedSearchParameterException(SearchParameterInfo searchParameter)
    : SearchParameterNotSupportedException(searchParameter.Code)
{
    public SearchParameterInfo SearchParameter { get; } = searchParameter;
}
