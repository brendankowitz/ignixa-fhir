// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>One loaded source in <c>terminology_list</c>.</summary>
/// <param name="Name"><c>id@version</c> or <c>folder</c>.</param>
/// <param name="Origin"><c>package-store</c>, <c>fhir-package-cache</c>, <c>downloaded</c> or <c>folder</c>.</param>
/// <param name="FhirVersions">The FHIR versions the package declares.</param>
/// <param name="CodeSystems">CodeSystems that provide concepts.</param>
/// <param name="CodeSystemsWithoutConcepts">CodeSystems that do not (<c>content = not-present</c> stubs such as SNOMED CT and LOINC in core, and supplements); their systems count as not loaded.</param>
/// <param name="ValueSets">ValueSets.</param>
/// <param name="ConceptMaps">ConceptMaps.</param>
/// <param name="Files">What reading the source found.</param>
/// <param name="LoadMilliseconds">Wall time spent reading and parsing it.</param>
public sealed record TermSourceSummary(string Name, string Origin, IReadOnlyList<string> FhirVersions, int CodeSystems, int CodeSystemsWithoutConcepts, int ValueSets, int ConceptMaps, SourceCounts Files, long LoadMilliseconds)
{
    public static TermSourceSummary Of(LoadedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new TermSourceSummary(
            source.Info.Name,
            source.Info.Origin,
            source.FhirVersions,
            source.CodeSystems.Count(entry => entry.ProvidesConcepts),
            source.CodeSystems.Count(entry => !entry.ProvidesConcepts),
            source.ValueSets.Count,
            source.ConceptMaps.Count,
            source.Counts,
            source.LoadMilliseconds);
    }
}
