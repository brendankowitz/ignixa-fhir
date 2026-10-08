// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;

/// <summary>
/// Output of <see cref="BulkDeleteOrchestration"/>. The job row is authoritative; this output is only the
/// fallback the status handler reads when the job row was not finalized.
/// </summary>
/// <param name="Success">The job completed and its Completed outcome was persisted.</param>
/// <param name="Superseded">
/// The job was already terminal (typically cancelled) when this orchestration tried to record progress or
/// its outcome, so nothing further was persisted.
/// </param>
/// <param name="ResourceDeletedCount">Resources deleted by this orchestration, keyed by resource type.</param>
/// <param name="ErrorMessage">The failure that stopped the job; null on success or supersession.</param>
public record BulkDeleteOrchestrationOutput(
    bool Success,
    bool Superseded,
    Dictionary<string, long> ResourceDeletedCount,
    string? ErrorMessage);
