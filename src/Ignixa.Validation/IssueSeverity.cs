// <copyright file="IssueSeverity.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation;

/// <summary>
/// Severity levels for validation issues.
/// </summary>
public enum IssueSeverity
{
    /// <summary>
    /// Informational message - does not prevent resource processing.
    /// </summary>
    Information,

    /// <summary>
    /// Warning - indicates potential issues but does not prevent processing.
    /// </summary>
    Warning,

    /// <summary>
    /// Error - indicates a validation failure that should prevent processing.
    /// </summary>
    Error,

    /// <summary>
    /// Fatal - indicates a critical validation failure.
    /// </summary>
    Fatal,
}
