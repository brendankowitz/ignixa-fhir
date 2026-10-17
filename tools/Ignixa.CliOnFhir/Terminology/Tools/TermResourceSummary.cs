// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>One loaded CodeSystem, ValueSet or ConceptMap in a listing.</summary>
/// <param name="Url">The canonical url.</param>
/// <param name="Version">The version.</param>
/// <param name="Source"><c>id@version</c> or <c>folder</c>.</param>
/// <param name="Content">For a CodeSystem, its content mode.</param>
/// <param name="Concepts">For a CodeSystem, its concept count.</param>
/// <param name="HierarchyMeaning">For a CodeSystem, its hierarchyMeaning when declared.</param>
public sealed record TermResourceSummary(string Url, string? Version, string Source, string? Content, int? Concepts, string? HierarchyMeaning)
{
    public static TermResourceSummary Of(CodeSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new TermResourceSummary(entry.Url, entry.Version, entry.Origin.Source.Name, entry.Content, entry.InOrder.Count, entry.HierarchyMeaning);
    }
}
