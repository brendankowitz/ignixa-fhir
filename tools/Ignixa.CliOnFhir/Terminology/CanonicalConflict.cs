// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Two sources defining the same <c>url|version</c> with different content; answers use <paramref name="Kept"/>.</summary>
public sealed record CanonicalConflict(string ResourceType, string Url, string? Version, ResourceOrigin Kept, ResourceOrigin Other)
{
    public const string Code = "canonical-conflict";

    public IssueItem ToIssue() => new(
        "warning",
        Code,
        $"{ResourceType} {TerminologyNames.Canonical(Url, Version)} is defined differently by {Describe(Kept)} and by {Describe(Other)}. "
            + $"Answers use {Kept.Source.Name}: the folder comes first, then packages in the order they were loaded.",
        Kept.ToEvidence().File);

    private static string Describe(ResourceOrigin origin) => $"{origin.Source.Name} ({origin.Source.EvidencePrefix}{TerminologyNames.Path(origin.File)})";
}
