// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// The terminology resources of one package version that are waiting to be imported.
/// <para>
/// Grouped by package because that is the unit a terminology import orchestration is started for: both
/// callers turn one of these into a single <c>TerminologyImportTriggeredEvent</c>. A flat list of resource
/// ids would force the caller that scans every package to re-group it.
/// </para>
/// </summary>
/// <param name="PackageId">NPM package identifier (e.g. "hl7.fhir.us.core").</param>
/// <param name="PackageVersion">NPM package version (e.g. "5.0.1").</param>
/// <param name="PackageResourceIds">Ids of the CodeSystem, ValueSet and ConceptMap resources awaiting import.</param>
public sealed record PendingTerminologyImport(
    string PackageId,
    string PackageVersion,
    IReadOnlyList<long> PackageResourceIds);
