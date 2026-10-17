// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>The terminology resources one source provided, as read once; immutable.</summary>
/// <param name="Info">The source.</param>
/// <param name="FhirVersions">The FHIR versions the package manifest declares (empty for the folder or when undeclared).</param>
/// <param name="CodeSystems">CodeSystems in file order.</param>
/// <param name="ValueSets">ValueSets in file order.</param>
/// <param name="ConceptMaps">ConceptMaps in file order.</param>
/// <param name="Counts">What reading found.</param>
/// <param name="Problems">Data problems met while reading, as warnings.</param>
/// <param name="LoadMilliseconds">Wall time spent reading and parsing the source (download excluded).</param>
public sealed record LoadedSource(
    TerminologySourceInfo Info,
    IReadOnlyList<string> FhirVersions,
    IReadOnlyList<CodeSystemEntry> CodeSystems,
    IReadOnlyList<ValueSetEntry> ValueSets,
    IReadOnlyList<ConceptMapEntry> ConceptMaps,
    SourceCounts Counts,
    IReadOnlyList<IssueItem> Problems,
    long LoadMilliseconds);
