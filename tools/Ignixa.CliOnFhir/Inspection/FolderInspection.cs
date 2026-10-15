// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Facts returned by <c>folder_inspect</c> / <c>cli-on-fhir inspect</c>.</summary>
/// <param name="Root">Absolute path of the folder.</param>
/// <param name="FhirVersion">FHIR version the folder was indexed as.</param>
/// <param name="FilesScanned">Files with a supported extension that were read.</param>
/// <param name="FilesSkipped">Files ignored because of their extension.</param>
/// <param name="ContainerBundles">Bundles whose entries were catalogued individually.</param>
/// <param name="ResourceCount">Resources indexed.</param>
/// <param name="PatientCount">Patient resources indexed.</param>
/// <param name="ResourcesWithSyntheticIds">Resources that had no <c>id</c>.</param>
/// <param name="IndexEntryCount">Search index entries extracted across all resources.</param>
/// <param name="ResourceTypes">Indexed resources per type, most frequent first.</param>
/// <param name="DiagnosticCounts">Catalog and index diagnostics per code.</param>
/// <param name="Build">Cost of building the catalog and index.</param>
public sealed record FolderInspection(
    string Root,
    string FhirVersion,
    int FilesScanned,
    int FilesSkipped,
    int ContainerBundles,
    int ResourceCount,
    int PatientCount,
    int ResourcesWithSyntheticIds,
    long IndexEntryCount,
    IReadOnlyList<ResourceTypeCount> ResourceTypes,
    IReadOnlyDictionary<string, int> DiagnosticCounts,
    BuildStats Build);
