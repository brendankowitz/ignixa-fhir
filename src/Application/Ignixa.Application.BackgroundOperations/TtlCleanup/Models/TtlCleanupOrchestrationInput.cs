// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Models;

/// <summary>
/// Input for the TTL cleanup orchestration.
/// </summary>
/// <param name="BatchSize">Maximum number of expired resources to process per tenant in a single run.</param>
public record TtlCleanupOrchestrationInput(int BatchSize = 100);
