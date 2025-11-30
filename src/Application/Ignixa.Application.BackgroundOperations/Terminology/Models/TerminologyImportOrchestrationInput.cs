// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Terminology.Models;

/// <summary>
/// Input for terminology import orchestration.
/// Contains package metadata and list of PackageResource IDs to import.
/// </summary>
public record TerminologyImportOrchestrationInput(
    int TenantId,
    string PackageId,
    string PackageVersion,
    IReadOnlyList<long> PackageResourceIds);
