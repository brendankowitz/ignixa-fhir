// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>
/// Resources read for one response, within the size budget, with the evidence and problems that came with them.
/// </summary>
/// <param name="Resources">The resources, in the order requested; trimmed when a projection was given.</param>
/// <param name="Evidence">Where each returned resource lives.</param>
/// <param name="Issues">Entries that could not be read, and the resource that stopped the read at the size budget.</param>
/// <param name="Consumed">
/// How many of the requested entries were dealt with, returned or reported as a problem. Fewer than requested means
/// the size budget stopped the read; the remainder starts at this position.
/// </param>
public sealed record ResourceBatch(
    IReadOnlyList<JsonNode> Resources,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<IssueItem> Issues,
    int Consumed)
{
    /// <summary>The hard cap on the serialized resources of one response (50 KB, see tool-design.md).</summary>
    public const int SizeBudgetBytes = 50 * 1024;

    /// <summary>
    /// Reads <paramref name="entries"/> in order until all are read or the next resource would push the serialized
    /// total past <see cref="SizeBudgetBytes"/>. A first resource that alone exceeds the budget is not returned.
    /// </summary>
    /// <param name="projection">Trims each resource when given; the budget applies to the trimmed size.</param>
    public static ResourceBatch Collect(string rootPath, IReadOnlyList<CatalogEntry> entries, ElementsProjection? projection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        List<JsonNode> resources = [];
        List<EvidenceItem> evidence = [];
        List<IssueItem> issues = [];
        int used = 0;
        int consumed = 0;
        foreach (CatalogEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ResourceReadResult read = ResourceReader.Read(rootPath, entry);
            if (read.Resource is not { } source)
            {
                issues.Add(read.Problem!);
                consumed++;
                continue;
            }

            JsonObject resource = projection?.Apply(source) ?? source;
            int size = Encoding.UTF8.GetByteCount(resource.ToJsonString());
            if (used + size > SizeBudgetBytes)
            {
                string alone = used == 0 ? " even on its own" : string.Empty;
                issues.Add(new IssueItem(
                    "warning",
                    "size-budget",
                    $"{ResourceReader.Describe(entry)} is {size} bytes and does not fit the {SizeBudgetBytes}-byte response budget{alone}; request fewer resources at a time or trim them with 'elements'.",
                    entry.Line is int line ? $"{entry.FilePath}:{line}" : entry.FilePath));
                break;
            }

            used += size;
            consumed++;
            resources.Add(resource);
            evidence.Add(new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset));
        }

        return new ResourceBatch(resources, evidence, issues, consumed);
    }
}
