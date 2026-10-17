// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>What the store holds for a code system url (and version).</summary>
/// <param name="Url">The system asked for.</param>
/// <param name="Version">The version asked for; null for any.</param>
/// <param name="Entry">The CodeSystem version that answers; null when none is loaded.</param>
/// <param name="LoadedVersions">Versions loaded with concepts (any precedence), for a version that is not among them.</param>
/// <param name="Stubs">Entries for the url that provide no concepts (<c>content = not-present</c> or a supplement).</param>
/// <param name="OtherVersions">
/// For a request without a version that had several loaded versions to choose from, an <c>other-versions-loaded</c>
/// information issue naming them and the rule that chose; null otherwise.
/// </param>
public sealed record CodeSystemResolution(string Url, string? Version, CodeSystemEntry? Entry, IReadOnlyList<CodeSystemEntry> LoadedVersions, IReadOnlyList<CodeSystemEntry> Stubs, Contracts.IssueItem? OtherVersions = null)
{
    public const string OtherVersionsCode = "other-versions-loaded";

    public const string Found = "found";
    public const string NotLoaded = "system-not-loaded";
    public const string VersionNotLoaded = "version-not-loaded";

    /// <summary><c>found</c>, <c>version-not-loaded</c> or <c>system-not-loaded</c> (which includes a content-not-present stub).</summary>
    public string Outcome => Entry is not null ? Found : LoadedVersions.Count > 0 ? VersionNotLoaded : NotLoaded;

    /// <summary>Why the system counts as not loaded when only a stub exists: its content mode and source.</summary>
    public string? StubReason => Entry is null && LoadedVersions.Count == 0 && Stubs.Count > 0
        ? $"{Stubs[0].Origin.Source.Name} declares it with content '{TerminologyNames.Token(Stubs[0].Content ?? "(none)")}', which provides no concepts"
        : null;
}
