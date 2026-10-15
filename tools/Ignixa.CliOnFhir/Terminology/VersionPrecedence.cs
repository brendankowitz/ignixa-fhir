// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Semver;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// Which loaded version answers a request that names no version: the folder's first, then a package loaded explicitly
/// before the default core package (<see cref="TerminologySourceInfo.Tier"/>); among the candidates of that tier, the highest
/// version when they can be ordered (all strict SemVer, or all declaring the same R5 <c>versionAlgorithm</c> of
/// <c>semver</c>, <c>integer</c>, <c>date</c> or <c>alpha</c>), otherwise the first loaded.
/// </summary>
internal static class VersionPrecedence
{
    public const string RuleText = "the folder first, then packages loaded explicitly over the default core package, then the highest version when every candidate is SemVer or they share a versionAlgorithm, else the first loaded";

    /// <param name="candidates">The candidates in load order.</param>
    public static T? Choose<T>(IReadOnlyList<T> candidates, Func<T, string?> version, Func<T, string?> algorithm, Func<T, TerminologySourceInfo> source)
        where T : class
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        int tier = candidates.Min(candidate => source(candidate).Tier);
        List<T> best = [.. candidates.Where(candidate => source(candidate).Tier == tier)];
        if (best.Count == 1 || Comparer(best, version, algorithm) is not { } compare)
        {
            return best[0];
        }

        // Keeps the first loaded among equal versions.
        return best.Aggregate((kept, next) => compare(version(next)!, version(kept)!) > 0 ? next : kept);
    }

    private static Comparison<string>? Comparer<T>(List<T> candidates, Func<T, string?> version, Func<T, string?> algorithm)
    {
        List<string?> versions = [.. candidates.Select(version)];
        if (versions.Exists(value => value is null))
        {
            return null;
        }

        List<string?> algorithms = [.. candidates.Select(algorithm).Distinct(StringComparer.Ordinal)];
        string? declared = algorithms.Count == 1 ? algorithms[0] : null;
        if (algorithms.Count > 1)
        {
            return null;
        }

        return declared switch
        {
            null or "semver" => versions.TrueForAll(value => SemVersion.TryParse(value, SemVersionStyles.Strict, out _))
                ? (left, right) => SemVersion.ComparePrecedence(SemVersion.Parse(left, SemVersionStyles.Strict), SemVersion.Parse(right, SemVersionStyles.Strict))
                : null,
            "integer" => versions.TrueForAll(value => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                ? (left, right) => long.Parse(left, CultureInfo.InvariantCulture).CompareTo(long.Parse(right, CultureInfo.InvariantCulture))
                : null,
            "date" => versions.TrueForAll(value => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                ? (left, right) => DateTimeOffset.Parse(left, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).CompareTo(DateTimeOffset.Parse(right, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
                : null,
            "alpha" => string.CompareOrdinal,
            _ => null,
        };
    }
}
