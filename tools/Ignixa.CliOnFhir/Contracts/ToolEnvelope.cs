// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// The response shape shared by every CLI verb and MCP tool (see
/// docs/features/local-cli/investigations/agent-tool-surface.md, "Response envelope").
/// </summary>
/// <typeparam name="TFacts">Tool-specific deterministic values.</typeparam>
/// <param name="Facts">Deterministic values; never model-written text.</param>
/// <param name="Evidence">Resources the facts were read from.</param>
/// <param name="Omitted">Everything left out of the response, with the reason.</param>
/// <param name="Issues">Problems with the request or the data in scope.</param>
/// <param name="Freshness">When the index was built and whether files changed since.</param>
public sealed record ToolEnvelope<TFacts>(
    TFacts Facts,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<OmittedItem> Omitted,
    IReadOnlyList<IssueItem> Issues,
    FreshnessInfo Freshness)
{
    /// <summary>Always null until summaries are explicitly enabled; reserved so the shape never changes.</summary>
    public object? Derived => null;
}
