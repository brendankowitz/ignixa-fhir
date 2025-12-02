// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Types of conditions for guards and transitions in scenario state machines.
/// </summary>
public enum ConditionType
{
    /// <summary>
    /// Condition based on patient age (e.g., patient is >= 18 years).
    /// </summary>
    Age,

    /// <summary>
    /// Condition based on patient gender (e.g., patient is male/female).
    /// </summary>
    Gender,

    /// <summary>
    /// Condition based on attribute existence (e.g., attribute "diabetes" exists).
    /// </summary>
    AttributeExists,

    /// <summary>
    /// Condition based on attribute value (e.g., attribute "severity" >= 3).
    /// </summary>
    AttributeValue,

    /// <summary>
    /// Condition based on observation value (e.g., latest glucose > 200 mg/dL).
    /// </summary>
    Observation,

    /// <summary>
    /// Condition based on date/time (e.g., current date is after 2020-01-01).
    /// </summary>
    Date,

    /// <summary>
    /// Condition based on resource existence (e.g., patient has an active condition).
    /// </summary>
    Resource
}
