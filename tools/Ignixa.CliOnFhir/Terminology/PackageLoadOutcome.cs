// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Terminology.Packages;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>What <see cref="TerminologySession.LoadPackageAsync"/> did.</summary>
/// <param name="Source">The package's loaded source (the earlier one when it was already loaded).</param>
/// <param name="Acquired">How it was found; null when it was already loaded and not read again.</param>
/// <param name="AlreadyLoaded">True when the session already had this package.</param>
/// <param name="State">The session's terminology after the call.</param>
public sealed record PackageLoadOutcome(LoadedSource Source, AcquiredPackage? Acquired, bool AlreadyLoaded, TerminologyState State);
