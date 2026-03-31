// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.GraphQl.Models;

public sealed class SearchConnectionResult
{
    public int? Count { get; init; }
    public int Offset { get; init; }
    public int Pagesize { get; init; }
    public IReadOnlyList<SearchEdge> Edges { get; init; } = [];
    public string? First { get; init; }
    public string? Previous { get; init; }
    public string? Next { get; init; }
    public string? Last { get; init; }
}
