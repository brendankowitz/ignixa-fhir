// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>
/// Bounds on what one package may make the tool read. The core and HL7 Terminology packages are well within them
/// (hl7.fhir.r4.core 4.0.1: 12.8 MB compressed, 49 MB expanded, 5,046 entries; hl7.terminology.r4 7.4.0: 4.9 MB,
/// 66 MB, 4,226 entries); a package over any of them is refused, never read partially.
/// </summary>
public static class PackageLimits
{
    public const int MaxCompressedBytes = 64 * 1024 * 1024;

    public const long MaxExpandedBytes = 512L * 1024 * 1024;

    public const int MaxEntries = 20_000;

    public const int MaxEntryBytes = 32 * 1024 * 1024;

    public const int MaxManifestBytes = 1024 * 1024;
}
