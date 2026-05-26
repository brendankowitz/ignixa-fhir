// <copyright file="TestFhirPackageLoader.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

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
