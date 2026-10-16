// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>The parts of a FHIR npm package the terminology store reads.</summary>
/// <param name="Name">package.json <c>name</c>.</param>
/// <param name="Version">package.json <c>version</c>.</param>
/// <param name="FhirVersions">package.json <c>fhirVersions</c> (or the singular <c>fhirVersion</c>); empty when undeclared.</param>
/// <param name="Files">The CodeSystem, ValueSet and ConceptMap files directly under <c>package/</c>, plus any that could not be sniffed, in archive order.</param>
/// <param name="OtherResources">JSON files of other resource types under <c>package/</c>, not kept.</param>
/// <param name="FilesIgnored">Entries never read: outside <c>package/</c>, in its subfolders (examples), not regular files, or not JSON.</param>
/// <param name="StructureDefinitions">
/// The StructureDefinition files directly under <c>package/</c>, in archive order, when the reader was asked to keep them
/// (for profile validation); otherwise empty, and they are counted in <paramref name="OtherResources"/>.
/// </param>
public sealed record PackageContents(string Name, string Version, IReadOnlyList<string> FhirVersions, IReadOnlyList<TerminologyFile> Files, int OtherResources, int FilesIgnored, IReadOnlyList<TerminologyFile>? StructureDefinitions = null)
{
    /// <summary>The kept StructureDefinitions; empty when none were kept.</summary>
    public IReadOnlyList<TerminologyFile> Profiles => StructureDefinitions ?? [];
}
