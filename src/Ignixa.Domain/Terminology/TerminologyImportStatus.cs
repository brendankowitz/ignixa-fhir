// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Terminology;

/// <summary>
/// Status of terminology import for a PackageResource.
/// Tracks the lifecycle of extracting CodeSystem/ValueSet/ConceptMap concepts into dedicated terminology tables.
/// </summary>
public enum TerminologyImportStatus
{
    /// <summary>
    /// This resource is not a terminology resource (e.g., StructureDefinition, SearchParameter).
    /// Import tracking does not apply.
    /// </summary>
    NotApplicable = 0,

    /// <summary>
    /// Terminology import is pending (resource loaded into PackageResource but concepts not yet extracted).
    /// This is the initial state after package load for CodeSystem/ValueSet/ConceptMap resources.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Terminology import is currently in progress (background job active).
    /// </summary>
    InProgress = 2,

    /// <summary>
    /// Terminology import completed successfully.
    /// All concepts/codes have been extracted to TermConcept/TermValueSetExpansion tables.
    /// </summary>
    Completed = 3,

    /// <summary>
    /// Terminology import failed (see ImportErrorMessage for details).
    /// System will fall back to JSON parsing for terminology operations.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Import was intentionally skipped (e.g., CodeSystem with content=not-present).
    /// </summary>
    Skipped = 5
}
