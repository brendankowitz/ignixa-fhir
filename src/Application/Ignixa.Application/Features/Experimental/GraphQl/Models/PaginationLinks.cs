// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.GraphQl.Models;

public sealed class PaginationLinks
{
    public string? Next { get; init; }
    public string? Previous { get; init; }
    public string? Self { get; init; }
}
