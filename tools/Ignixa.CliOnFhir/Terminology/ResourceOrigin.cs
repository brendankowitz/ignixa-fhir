// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Where one CodeSystem, ValueSet or ConceptMap was read from, for evidence.</summary>
/// <param name="Source">The package or folder it came from.</param>
/// <param name="ResourceType">CodeSystem, ValueSet or ConceptMap.</param>
/// <param name="Id">The resource's id as written; null when it has none.</param>
/// <param name="File">Path within the package (<c>package/CodeSystem-x.json</c>) or relative to the folder.</param>
/// <param name="Line">1-based NDJSON line in the folder; null otherwise.</param>
/// <param name="Offset">Byte offset of the resource within <paramref name="File"/>.</param>
public sealed record ResourceOrigin(TerminologySourceInfo Source, string ResourceType, string? Id, string File, int? Line, long Offset)
{
    /// <summary>The evidence entry: the resource named <c>Type/id</c> (id as its digest when not a FHIR id), its file prefixed with <c>id#version/</c> for a package.</summary>
    public EvidenceItem ToEvidence() => new(
        Id is null ? ResourceType : FhirIds.Reference(ResourceType, Id),
        Source.EvidencePrefix + File,
        Line,
        Offset);
}
