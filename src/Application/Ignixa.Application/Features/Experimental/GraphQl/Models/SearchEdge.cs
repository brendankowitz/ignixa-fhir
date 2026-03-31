// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.Application.Features.Experimental.GraphQl.Models;

public sealed class SearchEdge
{
    public required JsonElement Resource { get; init; }
    public string? Mode { get; init; }
    public decimal? Score { get; init; }
}
