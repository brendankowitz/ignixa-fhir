// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Facts returned by <c>folder_inspect</c> / <c>cli-on-fhir inspect</c>, answered from the catalog.</summary>
/// <param name="Root">Absolute path of the folder.</param>
/// <param name="FhirVersion">FHIR version the folder was read as.</param>
/// <param name="FilesScanned">Files with a supported extension that were read.</param>
/// <param name="FilesSkipped">Files ignored because of their extension.</param>
/// <param name="ContainerBundles">Bundles whose entries were catalogued individually.</param>
/// <param name="ResourceCount">Resources catalogued.</param>
/// <param name="PatientCount">Patient resources catalogued.</param>
/// <param name="ResourcesWithSyntheticIds">Resources that had no <c>id</c>.</param>
/// <param name="ResourceTypes">Catalogued resources per type, most frequent first.</param>
/// <param name="DiagnosticCounts">Diagnostics per code.</param>
/// <param name="Build">Cost of building the catalog.</param>
/// <param name="Index">Index sizes and timings; null unless the indexes were explicitly built.</param>
/// <param name="CodeSystems">The ten most used code systems (by codings in the resources' JSON), with names and distinct codes; folder_code_systems has the rest.</param>
/// <param name="Terminology">
/// How the folder's codes resolve against the loaded terminology, per system; <c>not loaded</c> (and nothing computed) until
/// a terminology call has loaded the store, so inspecting never triggers a package load or download.
/// </param>
public sealed record FolderInspection(
    string Root,
    string FhirVersion,
    int FilesScanned,
    int FilesSkipped,
    int ContainerBundles,
    int ResourceCount,
    int PatientCount,
    int ResourcesWithSyntheticIds,
    IReadOnlyList<ResourceTypeCount> ResourceTypes,
    IReadOnlyDictionary<string, int> DiagnosticCounts,
    BuildStats Build,
    IndexFacts? Index,
    IReadOnlyList<CodeSystemBrief> CodeSystems,
    TermFolderUsage Terminology);
