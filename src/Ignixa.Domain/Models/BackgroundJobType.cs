// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Job type discriminator for background job repository.
/// Maps to JobType field in BackgroundJob table for multi-purpose storage.
/// </summary>
public enum BackgroundJobType
{
    /// <summary>
    /// Undefined job type (default, should not be used).
    /// </summary>
    None = 0,

    /// <summary>
    /// FHIR bulk data export operation.
    /// </summary>
    Export = 1,

    /// <summary>
    /// FHIR bulk data import operation.
    /// </summary>
    Import = 2,

    /// <summary>
    /// FHIR validation operation (for future use).
    /// </summary>
    Validate = 3,

    /// <summary>
    /// Index rebuild operation (for future use).
    /// </summary>
    Reindex = 4,
}
