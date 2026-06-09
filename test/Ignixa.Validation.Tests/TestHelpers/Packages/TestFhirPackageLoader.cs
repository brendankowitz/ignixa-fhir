// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Downloads and extracts FHIR IG packages from <c>https://packages.fhir.org</c> for use in
/// validation tests. Downloads are cached on disk in a stable location so subsequent
/// test runs do not re-hit the network.
/// <para>
/// Cache directory resolution order:
/// </para>
/// <list type="number">
///   <item><c>IGNIXA_TEST_PACKAGE_CACHE</c> environment variable, if set.</item>
///   <item><c>%TEMP%/ignixa-test-package-cache</c> (default).</item>
/// </list>
/// <para>
/// Each call to <see cref="LoadAsync"/> with the same (packageId, version) returns the same
/// in-memory <see cref="TestFhirPackage"/> instance for the lifetime of the test process,
/// so callers can compare references and avoid redundant tar-extraction.
/// </para>
/// <para>
/// If a package is not cached and download fails (e.g. CI runs offline), <see cref="LoadAsync"/>
/// surfaces the underlying <see cref="HttpRequestException"/>. Tests that depend on a package
/// should be skipped or pre-warmed via a CI step in offline environments.
/// </para>
/// </summary>
public static class TestFhirPackageLoader
{
    private const string DefaultCacheSubfolder = "ignixa-test-package-cache";
    private const string CacheEnvironmentVariable = "IGNIXA_TEST_PACKAGE_CACHE";

    private static readonly ConcurrentDictionary<string, Task<TestFhirPackage>> Loaded =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Loads the CARIN BlueButton 2.1.0 IG package (<c>hl7.fhir.us.carin-bb</c>).
    /// This is the version referenced by the customer scenario fixtures under
    /// <c>TestData/CustomerScenarios/</c>.
    /// </summary>
    public static Task<TestFhirPackage> LoadCarinBlueButtonAsync(CancellationToken cancellationToken = default)
        => LoadAsync("hl7.fhir.us.carin-bb", "2.1.0", cancellationToken);

    /// <summary>
    /// Loads the US Core 6.1.0 IG package (<c>hl7.fhir.us.core</c>) - a stable,
    /// widely-deployed version against R4. Bump as needed by passing a different
    /// version to <see cref="LoadAsync"/>.
    /// </summary>
    public static Task<TestFhirPackage> LoadUsCoreAsync(CancellationToken cancellationToken = default)
        => LoadAsync("hl7.fhir.us.core", "6.1.0", cancellationToken);

    /// <summary>
    /// Loads the AU Core 1.0.0 IG package (<c>hl7.fhir.au.core</c>). Note that AU Core
    /// declares transitive dependencies on AU Base, HL7 Terminology, and the UV Extensions
    /// pack. Validation of AU Core resources typically requires all of those layered in;
    /// see <c>AuCoreValidatorFactory</c> for the canonical wiring.
    /// </summary>
    public static Task<TestFhirPackage> LoadAuCoreAsync(CancellationToken cancellationToken = default)
        => LoadAsync("hl7.fhir.au.core", "1.0.0", cancellationToken);

    /// <summary>
    /// Loads AU Base 5.0.0 (<c>hl7.fhir.au.base</c>), the foundation AU profiles that AU Core extends.
    /// </summary>
    public static Task<TestFhirPackage> LoadAuBaseAsync(CancellationToken cancellationToken = default)
        => LoadAsync("hl7.fhir.au.base", "5.0.0", cancellationToken);

    /// <summary>
    /// Loads the HL7 Terminology R4 package (<c>hl7.terminology.r4</c>, 6.2.0). Provides
    /// the canonical ValueSets and CodeSystems that international IGs (AU Core, US Core,
    /// CARIN-BB, etc.) all depend on.
    /// </summary>
    public static Task<TestFhirPackage> LoadHl7TerminologyR4Async(CancellationToken cancellationToken = default)
        => LoadAsync("hl7.terminology.r4", "6.2.0", cancellationToken);

    /// <summary>
    /// Loads the FHIR UV Extensions R4 package (<c>hl7.fhir.uv.extensions.r4</c>, 5.1.0).
    /// Provides common extension definitions referenced by jurisdiction IGs.
    /// </summary>
    public static Task<TestFhirPackage> LoadUvExtensionsR4Async(CancellationToken cancellationToken = default)
        => LoadAsync("hl7.fhir.uv.extensions.r4", "5.1.0", cancellationToken);

    /// <summary>
    /// Loads an arbitrary FHIR IG package by id and version. Results are memoized
    /// per (packageId, version) for the lifetime of the test process.
    /// </summary>
    /// <param name="packageId">NPM package id (e.g. <c>hl7.fhir.us.carin-bb</c>).</param>
    /// <param name="version">Package version (e.g. <c>2.1.0</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<TestFhirPackage> LoadAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentException.ThrowIfNullOrEmpty(version);

        var key = $"{packageId}|{version}";
        return Loaded.GetOrAdd(key, _ => LoadCoreAsync(packageId, version, cancellationToken));
    }

    /// <summary>
    /// Loads a package and all its transitive declared dependencies as a flat ordered list.
    /// The root package is first; dependencies follow in breadth-first discovery order.
    /// </summary>
    /// <param name="rootPackageId">Root package id (e.g. <c>hl7.fhir.au.core</c>).</param>
    /// <param name="rootVersion">Root package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Closure semantics:
    /// <list type="bullet">
    ///   <item>Deduplicates by package id (later occurrences ignored; first-discovered version wins).</item>
    ///   <item>Skips <c>hl7.fhir.r4.core</c> - the base R4 spec is provided by the in-process
    ///         <c>R4CoreSchemaProvider</c> and is not distributed as a downloadable tarball.</item>
    ///   <item>Tolerates individual dependency download failures: if a transitive package is unavailable
    ///         the loader skips it and continues, since the rest of the closure may still be useful.</item>
    /// </list>
    /// The returned list is suitable for direct hand-off to
    /// <c>PackageValidatorFactory.BuildR4(params TestFhirPackage[])</c>.
    /// </remarks>
    public static async Task<IReadOnlyList<TestFhirPackage>> LoadWithDependenciesAsync(
        string rootPackageId,
        string rootVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPackageId);
        ArgumentException.ThrowIfNullOrEmpty(rootVersion);

        var ordered = new List<TestFhirPackage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hl7.fhir.r4.core" };
        var queue = new Queue<(string id, string version)>();
        queue.Enqueue((rootPackageId, rootVersion));

        while (queue.Count > 0)
        {
            var (id, version) = queue.Dequeue();
            if (!seen.Add(id))
            {
                continue;
            }

            TestFhirPackage pkg;
            try
            {
                pkg = await LoadAsync(id, version, cancellationToken);
            }
            catch (Exception)
            {
                // Best-effort: a missing transitive dep shouldn't fail the whole closure.
                continue;
            }

            ordered.Add(pkg);

            if (pkg.Manifest.Dependencies is { Count: > 0 } deps)
            {
                foreach (var dep in deps)
                {
                    if (!seen.Contains(dep.Key))
                    {
                        queue.Enqueue((dep.Key, dep.Value));
                    }
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// Resolves the on-disk cache directory used for downloaded packages.
    /// </summary>
    public static string GetCacheDirectory()
    {
        var envOverride = Environment.GetEnvironmentVariable(CacheEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride;
        }

        return Path.Combine(Path.GetTempPath(), DefaultCacheSubfolder);
    }

    private static async Task<TestFhirPackage> LoadCoreAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        var cacheDirectory = GetCacheDirectory();
        Directory.CreateDirectory(cacheDirectory);

        var cacheManager = new PackageCacheManager(cacheDirectory, NullLogger<PackageCacheManager>.Instance);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var loader = new NpmPackageLoader(
            httpClient,
            cacheManager,
            options: null,
            NullLogger<NpmPackageLoader>.Instance);

        await using var packageStream = await loader.DownloadPackageAsync(packageId, version, cancellationToken);

        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        var extraction = await extractor.ExtractAsync(packageStream, cancellationToken);

        return new TestFhirPackage(extraction);
    }
}
