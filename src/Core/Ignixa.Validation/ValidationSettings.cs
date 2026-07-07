// <copyright file="ValidationSettings.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

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
    /// Gets or sets a value indicating whether contained resources are validated against their own
    /// schema. Maps to the reference validator's <c>validateContains</c> setting; when false
    /// (<c>IGNORE</c>) contained resources are not validated. Defaults to true.
    /// </summary>
    public bool ValidateContainedResources { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the security-checks mode is enabled. When on, string
    /// values that look like embedded HTML tags are rejected. Off by default (mode-gated).
    /// </summary>
    public bool SecurityChecks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether embedded HTML in markdown is rejected. When on, markdown
    /// values containing what looks like an HTML tag are flagged. Off by default (mode-gated).
    /// </summary>
    public bool NoHtmlInMarkdown { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether example URLs (example.org / acme.com) are rejected in
    /// URL-valued elements. Corresponds to the reference validator's non-spec (<c>examples: false</c>)
    /// mode. Off by default (mode-gated); when off, example URLs are permitted.
    /// </summary>
    public bool CheckExampleUrls { get; set; }

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
