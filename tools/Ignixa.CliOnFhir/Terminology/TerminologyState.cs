// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A session's terminology at one moment: the store snapshot and what went wrong loading it.</summary>
/// <param name="Store">The loaded sources.</param>
/// <param name="LoadIssues">Sources that could not be loaded (as warnings), and data problems in those that were; reported with every answer.</param>
/// <param name="InitialLoadMilliseconds">Wall time of the first load (folder resources plus configured or default packages, downloads included).</param>
/// <param name="HeapBytes">Managed heap after the most recent load, after a full collection.</param>
/// <param name="WorkingSetBytes">Process working set after the most recent load.</param>
public sealed record TerminologyState(TerminologyStore Store, IReadOnlyList<IssueItem> LoadIssues, long InitialLoadMilliseconds, long HeapBytes, long WorkingSetBytes)
{
    public const string NothingLoadedCode = "terminology-not-loaded";

    /// <summary>
    /// Whether answers are impossible: no source loaded at all while some failed to load. Answering "system not loaded"
    /// then would only restate the failure, so calls fail with the load problems instead.
    /// </summary>
    public bool IsUnusable => Store.Sources.Count == 0 && LoadIssues.Count > 0;

    /// <summary>The load problems as the errors of a call that cannot be answered.</summary>
    public IReadOnlyList<IssueItem> FailureIssues() =>
    [
        new IssueItem("error", NothingLoadedCode, "No terminology could be loaded, so this cannot be answered; the issues below say why. Load a package with terminology_load_package.", Location: null),
        .. LoadIssues,
    ];
}
