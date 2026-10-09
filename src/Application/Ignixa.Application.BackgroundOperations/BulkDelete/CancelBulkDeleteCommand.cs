// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Cancels a running <c>$bulk-delete</c> job: terminates its orchestration and marks it Cancelled.
/// Deletions already made are not rolled back.
/// </summary>
public record CancelBulkDeleteCommand(int TenantId, string JobId) : IRequest<CancelBulkDeleteOutcome>;
