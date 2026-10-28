// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Audit attribution captured from the HTTP request that started a background job, persisted with the
/// job definition so the job's terminal audit event carries the caller's identity and custom audit headers.
/// Header values are audit data: they must only be written to the audit channel.
/// </summary>
public sealed record BackgroundJobAuditContext
{
    public required string UserId { get; init; }

    public string? CorrelationId { get; init; }

    public IReadOnlyDictionary<string, string> CustomHeaders { get; init; } = new Dictionary<string, string>();
}
