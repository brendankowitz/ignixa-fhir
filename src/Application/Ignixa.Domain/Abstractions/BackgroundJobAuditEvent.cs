// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Audit event emitted, at most once and best-effort, when a background job (e.g. $export, $import) reaches a
/// terminal status. Emitted by whichever writer persists the terminal status first: job completion, cancellation,
/// or a status poll that reconciles a runtime outcome the job record had not yet recorded.
/// </summary>
public sealed record BackgroundJobAuditEvent
{
    /// <summary>
    /// Job kind, e.g. "Export" or "Import".
    /// </summary>
    public required string JobType { get; init; }

    public required string JobId { get; init; }

    public required int TenantId { get; init; }

    /// <summary>
    /// Terminal job status: Completed, Failed, or Cancelled.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// FHIR AuditEvent.outcome (0=Success, 4=Minor failure, 8=Serious failure).
    /// </summary>
    public required string Outcome { get; init; }

    /// <summary>
    /// User who started the job, or "unknown" when the job has no persisted attribution (created before audit capture).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Correlation ID of the kick-off request.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Job duration from creation to terminal status, when known.
    /// </summary>
    public double? DurationMs { get; init; }

    /// <summary>
    /// Custom audit headers (<c>X-IGNIXA-AUDIT-*</c> / <c>X-MS-AZUREFHIR-AUDIT-*</c>) supplied on the kick-off request.
    /// </summary>
    public IReadOnlyDictionary<string, string> CustomHeaders { get; init; } = new Dictionary<string, string>();
}
