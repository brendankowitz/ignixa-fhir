// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Reads a <c>$bulk-delete</c> job's status, reconciling non-terminal metadata with its orchestration.
/// Throws <see cref="KeyNotFoundException"/> when the job does not exist, is not a bulk-delete job, or
/// belongs to another tenant.
/// </summary>
public record GetBulkDeleteStatusQuery(int TenantId, string JobId) : IRequest<GetBulkDeleteStatusResult>;
