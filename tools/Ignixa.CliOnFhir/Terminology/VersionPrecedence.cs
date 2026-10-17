// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Semver;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// Which loaded definition answers a request that names no version, and which copy a conflict keeps:
/// <list type="number">
/// <item>The folder.</item>
/// <item>
/// Among packages, authority: for a canonical under <c>http://terminology.hl7.org/</c>, an <c>hl7.terminology.*</c> package
/// over every other package; core (<c>hl7.fhir.*.core</c>, loaded explicitly or by default) below every other package,
/// because it carries stale copies of terminology another package owns (core stamps THO-derived CodeSystems with its own
/// FHIR version, so a version comparison across packages would prefer them).
/// </item>
/// <item>
/// Among the candidates of the first-loaded package of that rank, the highest version when they can be ordered (all strict
/// SemVer, or all declaring the same R5 <c>versionAlgorithm</c> of <c>semver</c>, <c>integer</c>, <c>date</c> or
/// <c>alpha</c>). Versions are never compared across package ids.
/// </item>
/// <item>Otherwise load order.</item>
/// </list>
/// </summary>
internal static class VersionPrecedence
{
    public const string RuleText = "the folder first; then, among packages, hl7.terminology.* for terminology.hl7.org canonicals and core (hl7.fhir.*.core) last; then the highest version within one package; else load order";

    private const string TerminologyHl7Base = "http://terminology.hl7.org/";

    /// <summary>The authority rank of <paramref name="source"/> for <paramref name="url"/>, lower first.</summary>
    public static int Rank(TerminologySourceInfo source, string url)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(url);

        if (source.IsFolder)
        {
            return 0;
        }

        if (source.IsDefault || IsCore(source.PackageId!))
        {
            return 3;
        }

        return url.StartsWith(TerminologyHl7Base, StringComparison.Ordinal) && source.PackageId!.StartsWith("hl7.terminology.", StringComparison.Ordinal) ? 1 : 2;
    }

    /// <summary>Whether <paramref name="packageId"/> is a FHIR core package, such as <c>hl7.fhir.r4.core</c>.</summary>
    public static bool IsCore(string packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);

        string[] parts = packageId.Split('.');
        return parts is ["hl7", "fhir", { Length: > 0 }, "core"];
    }

    /// <param name="candidates">The candidates for <paramref name="url"/>.</param>
    /// <param name="url">The canonical url the candidates share, which decides authority.</param>
    /// <param name="version">A candidate's version.</param>
    /// <param name="algorithm">A candidate's declared <c>versionAlgorithm</c>.</param>
    /// <param name="source">A candidate's source.</param>
    /// <param name="loadOrder">A source's position in load order.</param>
    public static T? Choose<T>(IReadOnlyList<T> candidates, string url, Func<T, string?> version, Func<T, string?> algorithm, Func<T, TerminologySourceInfo> source, Func<TerminologySourceInfo, int> loadOrder)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return null;
        }

        int rank = candidates.Min(candidate => Rank(source(candidate), url));
        List<T> ranked = [.. candidates.Where(candidate => Rank(source(candidate), url) == rank).OrderBy(candidate => loadOrder(source(candidate)))];
        string? package = source(ranked[0]).PackageId;
        List<T> best = [.. ranked.Where(candidate => string.Equals(source(candidate).PackageId, package, StringComparison.Ordinal))];
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
