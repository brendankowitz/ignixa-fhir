// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Result of <see cref="CancelBulkDeleteCommand"/>.
/// </summary>
public enum CancelBulkDeleteOutcome
{
    /// <summary>The job was running or queued and is now Cancelled.</summary>
    Accepted,

    /// <summary>The job had already reached Completed, Failed or Cancelled; nothing changed.</summary>
    AlreadyTerminal,

    /// <summary>No bulk-delete job with this ID exists for the tenant.</summary>
    NotFound,
}
