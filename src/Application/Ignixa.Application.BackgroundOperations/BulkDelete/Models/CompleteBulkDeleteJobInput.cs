// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Input for <see cref="Activities.CompleteBulkDeleteJobActivity"/>.
/// </summary>
/// <param name="JobId">Job ID.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="Success">Completed when true; Failed otherwise.</param>
/// <param name="ResourceDeletedCount">Total deleted resources, keyed by resource type.</param>
/// <param name="ErrorMessage">The failure; required when <paramref name="Success"/> is false.</param>
public record CompleteBulkDeleteJobInput(
    string JobId,
    int TenantId,
    bool Success,
    Dictionary<string, long> ResourceDeletedCount,
    string? ErrorMessage);
