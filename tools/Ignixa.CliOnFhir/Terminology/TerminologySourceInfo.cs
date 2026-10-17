// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One source of terminology resources: a FHIR npm package, or the data folder itself.</summary>
/// <param name="Name">How answers name the source: <c>id@version</c> for a package, <c>folder</c> for the data folder.</param>
/// <param name="PackageId">The package id; null for the folder.</param>
/// <param name="PackageVersion">The exact package version; null for the folder.</param>
/// <param name="Origin">
/// Where the content was read from: <c>package-store</c> (the tool's own package store under the cache root),
/// <c>fhir-package-cache</c> (the standard <c>~/.fhir/packages</c> cache, read only), <c>downloaded</c> (fetched from
/// packages.fhir.org by this call) or <c>folder</c>.
/// </param>
/// <param name="Location">The package file or directory read, or the data folder; for diagnostics only.</param>
/// <param name="IsDefault">True for the FHIR version's core package loaded because no package was configured. Like every core package it ranks below every other package (<see cref="VersionPrecedence"/>).</param>
public sealed record TerminologySourceInfo(string Name, string? PackageId, string? PackageVersion, string Origin, string Location, bool IsDefault = false)
{
    public const string FolderName = "folder";

    public bool IsFolder => PackageId is null;

    /// <summary>The prefix evidence gives a file of this source: <c>id#version/</c> (the path a FHIR package cache uses), or nothing for the folder.</summary>
    public string EvidencePrefix => PackageId is null ? string.Empty : $"{PackageId}#{PackageVersion}/";

    public static TerminologySourceInfo Folder(string rootPath) => new(FolderName, null, null, FolderName, rootPath);

    public static TerminologySourceInfo Package(string id, string version, string origin, string location, bool isDefault = false) =>
        new($"{id}@{version}", id, version, origin, location, isDefault);
}
