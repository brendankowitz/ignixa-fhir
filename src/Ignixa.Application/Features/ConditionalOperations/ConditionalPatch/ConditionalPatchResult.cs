// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalPatch;

/// <summary>
/// Result of a conditional patch operation.
/// </summary>
/// <param name="Resource">The patched resource</param>
/// <param name="MatchCount">Number of resources that matched the search criteria (should always be 1 for success)</param>
public record ConditionalPatchResult(
    ResourceWrapper Resource,
    int MatchCount);
