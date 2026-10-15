// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Facts of <c>terminology_load_package</c>.</summary>
/// <param name="Package">The package loaded, <c>id@version</c>.</param>
/// <param name="Requested">What was asked for.</param>
/// <param name="VersionFrom"><c>requested</c>, or <c>registry-latest</c> when a bare id was resolved to the registry's latest version.</param>
/// <param name="Origin"><c>package-store</c>, <c>fhir-package-cache</c> or <c>downloaded</c> (from packages.fhir.org by this call).</param>
/// <param name="AlreadyLoaded">True when the session already had it; nothing was read again.</param>
/// <param name="Source">What the package provided.</param>
/// <param name="ElapsedMilliseconds">Wall time of this call, download included (and, on the first terminology call, the initial load).</param>
/// <param name="CodeSystems">The CodeSystems it provided (at most 50 here; terminology_list pages the rest).</param>
public sealed record TermLoadFacts(
    string Package,
    string Requested,
    string VersionFrom,
    string Origin,
    bool AlreadyLoaded,
    TermSourceSummary Source,
    long ElapsedMilliseconds,
    IReadOnlyList<TermResourceSummary> CodeSystems);
