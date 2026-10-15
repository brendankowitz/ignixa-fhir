// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Packages;

/// <summary>The sidecar written next to a stored package archive: where it came from and the digests to re-check it by.</summary>
/// <param name="Id">The package id.</param>
/// <param name="Version">The exact version.</param>
/// <param name="Source">The URL the archive was downloaded from.</param>
/// <param name="Shasum">The SHA-1 the registry published for the archive (hex), which the download was checked against.</param>
/// <param name="Sha512">The SHA-512 of the archive as stored (hex), checked on every read.</param>
public sealed record StoredPackageRecord(string Id, string Version, string Source, string Shasum, string Sha512);
