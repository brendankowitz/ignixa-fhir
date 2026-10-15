// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Terminology.Packages;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>How a session finds terminology.</summary>
public sealed record TerminologyOptions
{
    /// <summary>No packages configured, no package store, no FHIR package cache, no network: only the folder's own terminology resources.</summary>
    public static TerminologyOptions None { get; } = new();

    /// <summary>Packages to load on the first terminology call (<c>--terminology</c>); when empty, the FHIR version's core package is loaded.</summary>
    public IReadOnlyList<PackageRequest> Packages { get; init; } = [];

    /// <summary>The tool's package store; null when packages are not stored.</summary>
    public PackageStore? Store { get; init; }

    /// <summary>The standard FHIR package cache directory (<c>~/.fhir/packages</c>), read only; null when not read.</summary>
    public string? FhirPackageCache { get; init; }

    /// <summary>The FHIR package registry client; null when the network is disabled (<c>--offline</c>).</summary>
    public FhirPackageRegistry? Registry { get; init; }

    public bool Offline => Registry is null;
}
