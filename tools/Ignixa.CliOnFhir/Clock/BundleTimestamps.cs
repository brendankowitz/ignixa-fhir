// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// The <c>Bundle.timestamp</c> of the folder's container Bundles (the JSON files whose entries the catalog holds), read without
/// reading any Bundle whole. The catalog records where each entry's resource lies, so a Bundle's own members (<c>timestamp</c>
/// among them) are either before its first catalogued entry or after its last: only those two ranges are read, each at most
/// <see cref="MaxRegionBytes"/>, and the second is followed to the end of the <c>entry</c> array, whatever entries the catalog
/// did not keep (<see cref="BundleRoot"/>). When the Bundles' timestamps differ the latest is used, and an information issue gives
/// the spread and how many Bundles had none or one that could not be read.
/// </summary>
internal static class BundleTimestamps
{
    /// <summary>The most bytes read before a Bundle's first entry, and after its last.</summary>
    public const int MaxRegionBytes = 1024 * 1024;

    /// <summary>The latest timestamp of the folder's container Bundles, or null when none states one; and the notes.</summary>
    public static (DateTimeOffset? Latest, IReadOnlyList<IssueItem> Issues) Detect(FolderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        // A Bundle entry is never at offset 0 (the Bundle's own brace is), and only a .json file's entries have no line.
        List<(string File, long First, long End)> bundles = [.. catalog.Entries
            .Where(entry => entry.Line is null && entry.Offset > 0)
            .GroupBy(entry => entry.FilePath, StringComparer.Ordinal)
            .Select(group => (group.Key, group.Min(entry => entry.Offset), group.Max(entry => entry.Offset + entry.Length)))
            .OrderBy(bundle => bundle.Key, StringComparer.Ordinal)];

        List<DateTimeOffset> stamps = [];
        int none = 0;
        int unread = Math.Max(0, catalog.ContainerBundles - bundles.Count);
        foreach ((string file, long first, long end) in bundles)
        {
            BundleRoot.Timestamp read = Read(Path.Combine(catalog.RootPath, file), first, end);
            if (read.Outcome == BundleRoot.Outcome.None)
            {
                none++;
            }
            else if (read.Outcome == BundleRoot.Outcome.Found && FhirInstant.TryParse(read.Text, out DateTimeOffset stamp))
            {
                stamps.Add(stamp);
            }
            else
            {
                unread++;
            }
        }

        if (stamps.Count == 0)
        {
            return (null, unread == 0 ? [] : [Note(stamps, none, unread, latest: null)]);
        }

        DateTimeOffset latest = stamps.MaxBy(stamp => stamp.UtcDateTime);
        bool agree = stamps.Select(stamp => stamp.UtcDateTime).Distinct().Count() == 1 && none == 0 && unread == 0;
        return (latest, agree ? [] : [Note(stamps, none, unread, latest)]);
    }

    private static IssueItem Note(List<DateTimeOffset> stamps, int none, int unread, DateTimeOffset? latest)
    {
        string unreadText = unread == 0 ? string.Empty : $" {unread} Bundles' timestamps could not be read (one that is not a FHIR instant, a Bundle with no catalogued entries, or more than {MaxRegionBytes} bytes of its own members before its first entry or after its last).";
        if (latest is not { } used)
        {
            return new IssueItem("information", "as-of-bundle-timestamps", $"No container Bundle states a timestamp that could be read, so asOf is the clock's.{unreadText}", Location: null);
        }

        string earliest = FhirInstant.Format(stamps.MinBy(stamp => stamp.UtcDateTime));
        string noneText = none == 0 ? string.Empty : $" {none} Bundles state no timestamp.";
        return new IssueItem(
            "information",
            "as-of-bundle-timestamps",
            $"{stamps.Count} container Bundles state a timestamp, from {earliest} to {FhirInstant.Format(used)}; asOf is the latest.{noneText}{unreadText}",
            Location: null);
    }

    // Before the entries first; then after them. "None" holds only when both ranges were read whole and neither has a timestamp.
    private static BundleRoot.Timestamp Read(string path, long first, long end)
    {
        try
        {
            byte[] before = FileRegion.Read(path, 0, (int)Math.Min(first, MaxRegionBytes), out _);
            BundleRoot.Timestamp leading = BundleRoot.BeforeEntries(before);
            if (leading.Outcome == BundleRoot.Outcome.Found)
            {
                return leading;
            }

            byte[] after = FileRegion.Read(path, end, MaxRegionBytes, out bool truncated);
            BundleRoot.Timestamp trailing = truncated ? new BundleRoot.Timestamp(BundleRoot.Outcome.Unknown, null) : BundleRoot.AfterEntries(after);
            return trailing.Outcome == BundleRoot.Outcome.None && leading.Outcome != BundleRoot.Outcome.None
                ? new BundleRoot.Timestamp(BundleRoot.Outcome.Unknown, null)
                : trailing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BundleRoot.Timestamp(BundleRoot.Outcome.Unknown, null);
        }
    }
}
