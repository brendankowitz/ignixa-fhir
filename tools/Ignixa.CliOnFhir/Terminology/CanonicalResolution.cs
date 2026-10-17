// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>What the store holds for a ValueSet or ConceptMap canonical.</summary>
/// <param name="Url">The url asked for.</param>
/// <param name="Version">The version asked for; null for any.</param>
/// <param name="Entry">The version that answers; null when none is loaded.</param>
/// <param name="LoadedVersions">The versions loaded for the url, by precedence.</param>
/// <param name="OtherVersions">An <c>other-versions-loaded</c> information issue when a request without a version had several candidates; null otherwise.</param>
public sealed record CanonicalResolution<T>(string Url, string? Version, T? Entry, IReadOnlyList<string?> LoadedVersions, Contracts.IssueItem? OtherVersions = null)
    where T : class;
