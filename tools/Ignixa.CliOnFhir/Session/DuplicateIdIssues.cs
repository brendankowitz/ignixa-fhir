// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Session;

/// <summary>
/// The <c>duplicate-id</c> warning every tool that reads a resource type carries for that type: the catalog keeps the
/// first resource with a given type and id and drops the rest, so without it an answer could silently come from the
/// "wrong" copy (for example an id-less Bundle entry whose RESTful fullUrl gives it the id of a resource in another file).
/// </summary>
internal static class DuplicateIdIssues
{
    public const string Code = "duplicate-id";

    /// <summary>Most duplicates named per type; the count covers them all.</summary>
    public const int SampleLimit = 5;

    /// <summary>One warning per type among <paramref name="resourceTypes"/> that has dropped duplicates, in ordinal type order.</summary>
    public static IEnumerable<IssueItem> For(FolderCatalog catalog, IEnumerable<string> resourceTypes)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(resourceTypes);

        if (catalog.Duplicates.Count == 0)
        {
            yield break;
        }

        foreach (string type in resourceTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            List<CatalogDuplicate> duplicates = [.. catalog.Duplicates.Where(duplicate => duplicate.ResourceType == type)];
            if (duplicates.Count == 0)
            {
                continue;
            }

            string more = duplicates.Count > SampleLimit ? $" (first {SampleLimit})" : string.Empty;
            string samples = string.Join("; ", duplicates.Take(SampleLimit).Select(duplicate => $"{FhirIds.Reference(type, duplicate.Id)} used from {duplicate.Kept}, ignored in {duplicate.Ignored}"));
            yield return new IssueItem(
                "warning",
                Code,
                $"{duplicates.Count} {type} resources repeat the type and id of an earlier one and were dropped, so every answer about {type} comes from the first occurrence only{more}: {samples}.",
                duplicates[0].Ignored);
        }
    }
}
