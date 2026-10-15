// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Packages;

/// <summary>A package read and checked against the identity asked for.</summary>
/// <param name="Id">The package id.</param>
/// <param name="Version">The exact version.</param>
/// <param name="Contents">What was read.</param>
/// <param name="Origin"><c>package-store</c>, <c>fhir-package-cache</c> or <c>downloaded</c>.</param>
/// <param name="Location">The archive or directory read, or the URL downloaded from.</param>
/// <param name="VersionFromRegistry">True when no version was asked for and the registry's latest was used.</param>
/// <param name="Warnings">Problems that did not stop the load, for example a package store that could not be written.</param>
public sealed record AcquiredPackage(string Id, string Version, PackageContents Contents, string Origin, string Location, bool VersionFromRegistry, IReadOnlyList<IssueItem> Warnings);
