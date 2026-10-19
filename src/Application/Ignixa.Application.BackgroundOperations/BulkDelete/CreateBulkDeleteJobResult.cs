// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// The accepted <c>$bulk-delete</c> job. The ID addresses both the job row and its orchestration instance.
/// </summary>
public record CreateBulkDeleteJobResult(string JobId);
