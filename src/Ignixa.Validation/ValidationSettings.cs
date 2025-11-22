// <copyright file="ValidationSettings.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Validation.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Validation;

/// <summary>
/// Configuration and services for validation execution.
/// </summary>
public class ValidationSettings
{
    /// <summary>
    /// Gets or sets the validation depth to execute (Minimal/Spec/Full).
    /// </summary>
    public ValidationDepth Depth { get; set; } = ValidationDepth.Spec;

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

    /// <summary>
    /// Unified depth (alias for backward compatibility).
    /// </summary>
    public ValidationDepth ValidationDepth
    {
        get => Depth;
        set => Depth = value;
    }
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
