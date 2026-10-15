// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Facts of <c>terminology_list</c>.</summary>
/// <param name="InitialLoadMilliseconds">Wall time of the first load (downloads included).</param>
/// <param name="HeapBytes">Managed heap after the most recent load.</param>
/// <param name="WorkingSetBytes">Process working set after the most recent load.</param>
/// <param name="Offline">Whether the network is disabled.</param>
/// <param name="TxServer">The remote terminology server asked for content not loaded locally; null when none is configured.</param>
/// <param name="Sources">The loaded sources in precedence order (the folder, then packages in load order).</param>
/// <param name="Kind">What is listed: <c>codeSystems</c>, <c>valueSets</c> or <c>conceptMaps</c>.</param>
/// <param name="Package">The source the listing is limited to, when given.</param>
/// <param name="Filter">The url substring the listing is limited to, when given.</param>
/// <param name="Total">Matching resources.</param>
/// <param name="Offset">Resources skipped.</param>
/// <param name="Returned">Resources in this page.</param>
/// <param name="Items">This page, in precedence then file order.</param>
/// <param name="FolderUsage">Codes the folder uses and how they resolve, when asked for.</param>
public sealed record TermListFacts(
    long InitialLoadMilliseconds,
    long HeapBytes,
    long WorkingSetBytes,
    bool Offline,
    string? TxServer,
    IReadOnlyList<TermSourceSummary> Sources,
    string Kind,
    string? Package,
    string? Filter,
    int Total,
    int Offset,
    int Returned,
    IReadOnlyList<TermResourceSummary> Items,
    TermFolderUsage? FolderUsage);
