// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// The physical effect a <c>$bulk-delete</c> job applies to each matched resource.
/// </summary>
public enum BulkDeleteMode
{
    /// <summary>
    /// Logical (FHIR) delete. Creates a new tombstone version per resource; prior versions are kept.
    /// </summary>
    SoftDelete = 0,

    /// <summary>
    /// Physical delete. Removes every version of the resource and its search indexes.
    /// </summary>
    HardDelete = 1,

    /// <summary>
    /// Physical delete of history only. Removes historical (non-current) versions and their search
    /// indexes; the current version (including a current soft-deleted tombstone) is kept.
    /// </summary>
    PurgeHistory = 2,
}
