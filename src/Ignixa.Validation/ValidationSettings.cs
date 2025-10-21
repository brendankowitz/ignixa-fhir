// <copyright file="ValidationSettings.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation;

/// <summary>
/// Configuration and services for validation execution.
/// </summary>
public class ValidationSettings
{
    /// <summary>
    /// Gets or sets the validation tier to execute (Fast/Spec/Profile).
    /// </summary>
    public ValidationTier Tier { get; set; } = ValidationTier.Spec;

    /// <summary>
    /// Gets or sets a value indicating whether terminology validation should be performed.
    /// </summary>
    public bool SkipTerminologyValidation { get; set; }

    /// <summary>
    /// Gets or sets the mode for handling terminology service failures.
    /// </summary>
    public TerminologyFailureMode TerminologyFailureMode { get; set; } = TerminologyFailureMode.Warning;

    /// <summary>
    /// Gets or sets the terminology service for code validation.
    /// If null, terminology validation will be skipped.
    /// </summary>
    public ITerminologyService? TerminologyService { get; set; }
}

/// <summary>
/// Validation tier levels.
/// </summary>
public enum ValidationTier
{
    /// <summary>
    /// No validation (skip validation).
    /// </summary>
    None = 0,

    /// <summary>
    /// Fast validation: JSON structure + required fields (&lt;25ms).
    /// </summary>
    Fast = 1,

    /// <summary>
    /// Spec validation: + Cardinality, types, FHIRPath invariants (&lt;200ms).
    /// </summary>
    Spec = 2,

    /// <summary>
    /// Profile validation: + Custom profiles, slicing, terminology (&lt;1000ms).
    /// </summary>
    Profile = 3
}

/// <summary>
/// How to handle terminology service failures.
/// </summary>
public enum TerminologyFailureMode
{
    /// <summary>
    /// Downgrade terminology failures to warnings.
    /// </summary>
    Warning,

    /// <summary>
    /// Treat terminology failures as errors.
    /// </summary>
    Error
}
