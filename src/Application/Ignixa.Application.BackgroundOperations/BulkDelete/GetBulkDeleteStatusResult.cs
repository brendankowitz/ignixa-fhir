// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// A bulk-delete job's status.
/// </summary>
/// <param name="Status">One of <c>Queued</c>, <c>Running</c>, <c>Completed</c>, <c>Failed</c>, <c>Cancelled</c>.</param>
/// <param name="ResourceDeletedCount">
/// Resources deleted so far (or in total, once terminal), keyed by resource type. Only types with a
/// positive count appear.
/// </param>
/// <param name="Issues">Failure diagnostics persisted with a failed job's result; empty otherwise.</param>
/// <param name="ErrorMessage">The failure message of a failed job; null otherwise.</param>
public record GetBulkDeleteStatusResult(
    string Status,
    IReadOnlyDictionary<string, long> ResourceDeletedCount,
    IReadOnlyList<string> Issues,
    string? ErrorMessage);
