// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Keeps terminology text and lists within the response budget, saying what was cut.</summary>
internal static class TermText
{
    /// <summary>Longest display or designation shown.</summary>
    public const int MaxShortText = 500;

    /// <summary>Longest definition or comment shown.</summary>
    public const int MaxLongText = 4_000;

    /// <summary>Most properties or designations listed for one concept.</summary>
    public const int MaxListed = 50;

    /// <summary>The serialized size facts may take, leaving room in the 50 KB envelope for issues, evidence and the rest.</summary>
    public const int FactsBudget = 40 * 1024;

    public static string? Clip(string? text, int max, string what, List<OmittedItem> omitted)
    {
        if (text is null || text.Length <= max)
        {
            return text;
        }

        omitted.Add(new OmittedItem($"{what}: {text.Length - max} of {text.Length} characters", "size-budget"));
        return text[..max];
    }

    public static IReadOnlyList<T> Take<T>(IReadOnlyList<T> items, string what, List<OmittedItem> omitted)
    {
        if (items.Count <= MaxListed)
        {
            return items;
        }

        omitted.Add(new OmittedItem($"{items.Count - MaxListed} more {what}", "limit"));
        return [.. items.Take(MaxListed)];
    }

    /// <summary>
    /// The largest prefix of <paramref name="page"/> whose facts fit <paramref name="budget"/>, the facts built from it, and an
    /// omitted note when items were dropped for size.
    /// </summary>
    public static (TFacts Facts, int Kept) FitPage<TItem, TFacts>(IReadOnlyList<TItem> page, Func<IReadOnlyList<TItem>, TFacts> build, string what, List<OmittedItem> omitted, int budget = FactsBudget)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(build);

        int kept = page.Count;
        TFacts facts = build(page);
        while (kept > 0 && EnvelopeJson.SizeOf(facts) > budget)
        {
            kept /= 2;
            facts = build([.. page.Take(kept)]);
        }

        if (kept < page.Count)
        {
            omitted.Add(new OmittedItem($"{page.Count - kept} {what} of this page", "size-budget"));
        }

        return (facts, kept);
    }
}
