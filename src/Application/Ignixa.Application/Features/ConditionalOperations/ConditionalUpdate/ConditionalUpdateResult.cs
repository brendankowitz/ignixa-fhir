// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate;

/// <summary>
/// Result of a conditional update operation.
/// </summary>
public record ConditionalUpdateResult(
    ResourceWrapper Resource,
    bool WasCreated,  // true = 201 Created, false = 200 OK (updated)
    int MatchCount);
