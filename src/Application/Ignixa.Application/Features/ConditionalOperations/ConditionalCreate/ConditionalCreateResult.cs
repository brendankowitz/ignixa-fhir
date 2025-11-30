// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalCreate;

/// <summary>
/// Result of a conditional create operation.
/// </summary>
/// <param name="Resource">The resource wrapper (either created or existing).</param>
/// <param name="WasCreated">True if a new resource was created (201 Created), false if existing resource was returned (200 OK).</param>
/// <param name="MatchCount">The number of resources that matched the search criteria.</param>
public record ConditionalCreateResult(
    ResourceWrapper Resource,
    bool WasCreated,
    int MatchCount);
