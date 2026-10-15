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
/// <param name="Issues">Entries that could not be read.</param>
/// <param name="Skipped">Resources that can never fit the budget, each named with how to read it; they were passed over, not postponed.</param>
/// <param name="Consumed">
/// How many of the requested entries were dealt with: returned, skipped or reported as a problem. Fewer than requested
/// means the size budget stopped the read; the remainder starts at this position.
/// </param>
public sealed record ResourceBatch(
    IReadOnlyList<JsonNode> Resources,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<IssueItem> Issues,
    IReadOnlyList<OmittedItem> Skipped,
    int Consumed)
{
    /// <summary>
    /// Room kept free for what is added to the envelope after the batch is read (cache issues, a freshness warning, the
    /// closing omitted item).
    /// </summary>
    public const int HeadroomBytes = 1024;

    /// <summary>
    /// Reads <paramref name="entries"/> in order until all are read or the next resource would push the whole serialized
    /// response past <see cref="EnvelopeJson.MaxBytes"/>. A resource that cannot fit even in an otherwise empty response
    /// is skipped and reported in <see cref="Skipped"/>, so paging moves past it instead of stalling on it.
    /// </summary>
    /// <param name="projection">Trims each resource when given; the budget applies to the trimmed size.</param>
    /// <param name="reservedBytes">The serialized size of the response without any of the batch's resources, evidence or issues.</param>
    public static ResourceBatch Collect(string rootPath, IReadOnlyList<CatalogEntry> entries, ElementsProjection? projection, int reservedBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        int limit = EnvelopeJson.MaxBytes - HeadroomBytes;
        List<JsonNode> resources = [];
        List<EvidenceItem> evidence = [];
        List<IssueItem> issues = [];
        List<OmittedItem> skipped = [];
        int used = reservedBytes;
        int consumed = 0;
        foreach (CatalogEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ResourceReadResult read = ResourceReader.Read(rootPath, entry);
            if (read.Resource is not { } source)
            {
                int issueSize = EnvelopeJson.SizeOf(read.Problem) + 1;
                if (used + issueSize > limit)
                {
                    break;
                }

                used += issueSize;
                issues.Add(read.Problem!);
                consumed++;
                continue;
            }

            JsonObject resource = projection?.Apply(source) ?? source;
            var item = new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset);
            int cost = Encoding.UTF8.GetByteCount(resource.ToJsonString(EnvelopeJson.Compact)) + EnvelopeJson.SizeOf(item) + 2;
            if (reservedBytes + cost > limit)
            {
                var notice = new OmittedItem($"{item.Reference} is {cost} bytes and exceeds the size budget; read it with read_resources (CLI: get) and elements", "size-budget");
                int noticeSize = EnvelopeJson.SizeOf(notice) + 1;
                if (used + noticeSize > limit)
                {
                    break;
                }

                used += noticeSize;
                skipped.Add(notice);
                consumed++;
                continue;
            }

            if (used + cost > limit)
            {
                break;
            }

            used += cost;
            consumed++;
            resources.Add(resource);
            evidence.Add(item);
        }

        return new ResourceBatch(resources, evidence, issues, skipped, consumed);
    }
}
