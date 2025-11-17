// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Terminology.Models;

/// <summary>
/// Output from terminology import orchestration.
/// Aggregates results from all imported resources.
/// </summary>
public record TerminologyImportOrchestrationOutput(
    bool Success,
    int TotalResourcesProcessed,
    int TotalConceptsImported,
    int SuccessCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<TerminologyImportResourceResult> Results,
    string? ErrorMessage,
    string? FailurePhase);

/// <summary>
/// Result for a single terminology resource import.
/// </summary>
public record TerminologyImportResourceResult(
    long PackageResourceId,
    string Canonical,
    string ResourceType,
    bool Success,
    int ConceptCount,
    string? ErrorMessage);
