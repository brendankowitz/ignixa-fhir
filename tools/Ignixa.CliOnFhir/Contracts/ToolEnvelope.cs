// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Ignixa.CliOnFhir.Clock;

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// The response shape shared by every CLI verb and MCP tool (see
/// docs/features/local-cli/investigations/agent-tool-surface.md, "Response envelope").
/// </summary>
/// <typeparam name="TFacts">Tool-specific deterministic values.</typeparam>
/// <param name="Facts">Deterministic values; never model-written text.</param>
/// <param name="Evidence">Resources the facts were read from; on the wire a table of files plus one item per resource naming its file by index (see <see cref="EvidenceJsonConverter"/>).</param>
/// <param name="Omitted">Everything left out of the response, with the reason.</param>
/// <param name="Absent">Things looked for and not found, each with the scope that was checked.</param>
/// <param name="Issues">Problems with the request or the data in scope.</param>
/// <param name="Freshness">When the index was built and whether files changed since.</param>
public sealed record ToolEnvelope<TFacts>(
    TFacts Facts,
    [property: JsonConverter(typeof(EvidenceJsonConverter))] IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<OmittedItem> Omitted,
    IReadOnlyList<AbsentItem> Absent,
    IReadOnlyList<IssueItem> Issues,
    FreshnessInfo Freshness)
{
    /// <summary>
    /// The instant the answer is anchored to (<see cref="AsOfClock"/>): the call's own <c>asOf</c>, else the session's, else what
    /// the tool defaults to (the patient's latest dated resource for the patient map, the clock for patient_cohort). Always
    /// written; null only when it cannot be told, because the session's <c>--as-of</c> is malformed (the call that read it says
    /// so) or the folder cannot be read to find its data-pull time.
    /// </summary>
    public AsOf? AsOf { get; init; }

    /// <summary>Always null until summaries are explicitly enabled; reserved so the shape never changes.</summary>
    public object? Derived => null;

    /// <summary>Suggested follow-up calls, built from structure only (see <see cref="NextCall"/>); empty when there are none.</summary>
    public IReadOnlyList<NextCall> Next { get; init; } = [];
}
