// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Sparky.Domain.Models;

namespace Sparky.Application.Features.Patient;

/// <summary>
/// Result of a Patient search query with streaming support.
/// </summary>
/// <param name="Resources">The async stream of matching Patient resources.</param>
/// <param name="Total">The total count of matching resources (if requested).</param>
/// <param name="ContinuationToken">Token for fetching the next page of results.</param>
public record SearchPatientResult(
    IAsyncEnumerable<ResourceWrapper> Resources,
    int? Total = null,
    string? ContinuationToken = null);
