// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>
/// Finds a package, in order: the tool's package store, the standard FHIR package cache (read only), then, unless offline,
/// the FHIR package registry, whose download is read and checked before it is stored. Only FHIR npm packages are
/// fetched, only from packages.fhir.org, and only for a package someone asked for (or the default core package).
/// </summary>
internal sealed class PackageAcquisition(PackageStore? store, string? fhirPackageCache, FhirPackageRegistry? registry, FolderOverlap overlap)
{
    public const string OfflineCode = "package-unavailable-offline";
    public const string IdentityMismatchCode = "package-identity-mismatch";
    public const string InvalidCode = "package-invalid";
    public const string StoreUnwritableCode = "package-store-unwritable";

    /// <param name="request">The package asked for.</param>
    /// <param name="cancellationToken">Cancels the acquisition.</param>
    /// <param name="keepStructureDefinitions">Also keep the package's StructureDefinitions (<see cref="PackageContents.Profiles"/>), for profile validation.</param>
    /// <exception cref="ToolRequestException">The package cannot be found, downloaded or read.</exception>
    public async Task<AcquiredPackage> AcquireAsync(PackageRequest request, CancellationToken cancellationToken, bool keepStructureDefinitions = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        RegistryPackage? document = null;
        string version;
        if (request.Version is { } exact)
        {
            version = exact;
        }
        else
        {
            document = await Document(request.Id, cancellationToken);
            version = document.Latest is { } latest
                ? ExactLatest(request.Id, latest)
                : throw ToolRequestException.Error(FhirPackageRegistry.NotFoundCode, $"The FHIR package registry names no latest version of {request.Id}; ask for an exact version.");
        }

        bool fromRegistry = request.Version is null;
        if (store?.TryRead(request.Id, version) is { } stored)
        {
            return Checked(request.Id, version, Read(stored, keepStructureDefinitions, cancellationToken), "package-store", store.ArchivePath(request.Id, version), fromRegistry, []);
        }

        if (fhirPackageCache is not null && Path.Combine(fhirPackageCache, $"{request.Id}#{version}") is { } cached && File.Exists(Path.Combine(cached, "package", PackageEntryFilter.ManifestName)))
        {
            PackageContents contents;
            try
            {
                contents = PackageDirectoryReader.Read(cached, cancellationToken, keepStructureDefinitions);
            }
            catch (PackageFormatException ex)
            {
                throw Invalid(request.Id, version, ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw ToolRequestException.Error(InvalidCode, $"The FHIR package cache copy of {request.Id}@{version} at '{cached}' cannot be read ({ex.GetType().Name}: {ex.Message}).");
            }

            return Checked(request.Id, version, contents, "fhir-package-cache", cached, fromRegistry, []);
        }

        document ??= await Document(request.Id, cancellationToken);
        if (!document.Shasums.TryGetValue(version, out string? shasum))
        {
            throw ToolRequestException.Error(FhirPackageRegistry.NotFoundCode, $"The FHIR package registry has no version {version} of {request.Id} (with a published shasum).");
        }

        byte[] archive = await registry!.DownloadAsync(request.Id, version, shasum, cancellationToken);
        AcquiredPackage downloaded = Checked(request.Id, version, Read(archive, keepStructureDefinitions, cancellationToken), "downloaded", FhirPackageRegistry.ArchiveUri(request.Id, version).AbsoluteUri, fromRegistry, []);
        return downloaded with { Warnings = Store(downloaded, archive, shasum) };
    }

    // The registry's dist-tags.latest is registry data: a value that is not an exact version is the registry's problem, said so.
    private static string ExactLatest(string id, string latest)
    {
        try
        {
            return PackageRequest.Exact(id, latest).Version!;
        }
        catch (ToolRequestException)
        {
            throw ToolRequestException.Error(PackageRequest.InvalidCode, $"The FHIR package registry {FhirPackageRegistry.BaseUri} names a latest version of {id} ('{TerminologyNames.Version(latest)}') that is not an exact SemVer version; ask for an exact version.");
        }
    }

    /// <summary>The versions of <paramref name="id"/> available without the network, for an offline request without a version.</summary>
    public IReadOnlyList<string> LocalVersions(string id)
    {
        IEnumerable<string> cached = fhirPackageCache is not null && Directory.Exists(fhirPackageCache)
            ? Directory.EnumerateDirectories(fhirPackageCache, id + "#*").Select(path => Path.GetFileName(path)[(id.Length + 1)..])
            : [];
        return [.. (store?.Versions(id) ?? []).Concat(cached).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private async Task<RegistryPackage> Document(string id, CancellationToken cancellationToken)
    {
        if (registry is null)
        {
            IReadOnlyList<string> local = LocalVersions(id);
            throw ToolRequestException.Error(
                OfflineCode,
                $"The network is disabled (--offline), so {id} can only be loaded from the package store or the FHIR package cache, by exact version"
                    + (local.Count == 0 ? "; neither holds any version of it." : $"; they hold {string.Join(", ", local.Select(version => $"{id}@{version}"))}."));
        }

        return await registry.GetPackageAsync(id, cancellationToken);
    }

    private IReadOnlyList<IssueItem> Store(AcquiredPackage package, byte[] archive, string shasum)
    {
        if (store is null)
        {
            return [new IssueItem("information", StoreUnwritableCode, $"{package.Id}@{package.Version} was downloaded but not stored (no package store is configured), so it will be downloaded again by another process.", Location: null)];
        }

        try
        {
            store.Save(new StoredPackageRecord(package.Id, package.Version, package.Location, shasum, Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(archive))), archive, overlap);
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [new IssueItem("warning", StoreUnwritableCode, $"{package.Id}@{package.Version} was downloaded and loaded but could not be stored in '{store.Directory}' ({ex.GetType().Name}: {ex.Message}); it will be downloaded again next time.", Location: null)];
        }
    }

    private static PackageContents Read(byte[] archive, bool keepStructureDefinitions, CancellationToken cancellationToken)
    {
        try
        {
            return PackageArchiveReader.Read(archive, cancellationToken, keepStructureDefinitions);
        }
        catch (PackageFormatException ex)
        {
            throw ToolRequestException.Error(InvalidCode, $"The package archive cannot be read: {ex.Message}");
        }
    }

    private static AcquiredPackage Checked(string id, string version, PackageContents contents, string origin, string location, bool fromRegistry, IReadOnlyList<IssueItem> warnings)
    {
        if (contents.Name != id || contents.Version != version)
        {
            throw ToolRequestException.Error(IdentityMismatchCode, $"The package read for {id}@{version} ({origin}, '{location}') declares a different name or version in its package.json; it was not loaded.");
        }

        return new AcquiredPackage(id, version, contents, origin, location, fromRegistry, warnings);
    }

    private static ToolRequestException Invalid(string id, string version, PackageFormatException ex) =>
        ToolRequestException.Error(InvalidCode, $"The FHIR package cache copy of {id}@{version} cannot be read: {ex.Message}");
}
