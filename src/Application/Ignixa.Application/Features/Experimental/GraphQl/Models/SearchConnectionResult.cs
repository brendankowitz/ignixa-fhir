// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.Application.Features.Experimental.GraphQl.Models;

public sealed class SearchConnectionResult
{
    public IReadOnlyList<JsonElement> Entries { get; init; } = [];
    public int? Total { get; init; }
    public PaginationLinks? Links { get; init; }
}
