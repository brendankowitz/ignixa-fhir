// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Session;

/// <summary>
/// Explanations for the names a response shows that the folder did not give: a resource whose id is not a FHIR id is named
/// by its digest (<see cref="FhirIds"/>), and one with no id by the id the catalog assigned it. Built from the resources the
/// response shows or summarizes, so a call that shows none of them carries neither.
/// </summary>
internal static class NameIssues
{
    /// <summary>The issue code for resources named by the id the catalog assigned them.</summary>
    public const string SyntheticCode = "synthetic-id";

    /// <summary>
    /// Per type, in ordinal order: an <see cref="FhirIds.InvalidCode"/> warning for the shown resources named by digest and a
    /// <see cref="SyntheticCode"/> information for those named by an assigned id, each located at the first and never
    /// echoing an id.
    /// </summary>
    public static IEnumerable<IssueItem> For(IEnumerable<CatalogEntry> shown)
    {
        ArgumentNullException.ThrowIfNull(shown);

        List<CatalogEntry> entries = [.. shown.DistinctBy(entry => (entry.ResourceType, entry.Id))];
        foreach (IGrouping<string, CatalogEntry> invalid in Of(entries.Where(entry => !entry.HasSyntheticId && !FhirIds.IsValid(entry.Id))))
        {
            yield return new IssueItem(
                "warning",
                FhirIds.InvalidCode,
                $"{invalid.Count()} {invalid.Key} resources shown have ids that are not FHIR ids (1 to 64 letters, digits, '-' or '.'), so every tool names each by a digest ({invalid.Key}/~ and 16 hex digits of the id's SHA-256), which get, search (_id, compartment) and the patient tools accept; the resources themselves are returned as written.",
                Location(invalid.First()));
        }

        foreach (IGrouping<string, CatalogEntry> synthetic in Of(entries.Where(entry => entry.HasSyntheticId)))
        {
            yield return new IssueItem(
                "information",
                SyntheticCode,
                $"{synthetic.Count()} {synthetic.Key} resources shown have no id, so the catalog named them: by the id of their Bundle entry's RESTful fullUrl, or else 'synthetic-' and a hash of their file and position. No other resource can reference a 'synthetic-' one.",
                Location(synthetic.First()));
        }
    }

    private static IEnumerable<IGrouping<string, CatalogEntry>> Of(IEnumerable<CatalogEntry> entries) =>
        entries.GroupBy(entry => entry.ResourceType, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal);

    private static string Location(CatalogEntry entry) => entry.Line is int line ? $"{entry.FilePath}:{line}" : $"{entry.FilePath}@{entry.Offset}";
}
