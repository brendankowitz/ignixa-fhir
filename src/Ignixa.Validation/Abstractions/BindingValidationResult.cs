// <copyright file="BindingValidationResult.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Result of validating a coded element against a terminology binding.
/// Includes severity based on binding strength and whether code was found.
/// </summary>
/// <param name="IsValid">True if code is valid in ValueSet or binding strength allows custom codes.</param>
/// <param name="Strength">Binding strength from ElementDefinition (Required, Extensible, Preferred, Example).</param>
/// <param name="Severity">Issue severity (ERROR for required violations, WARNING for extensible mismatches in mode=full).</param>
/// <param name="Message">Human-readable message describing validation result.</param>
/// <param name="SuggestedDisplay">Correct display value from CodeSystem if display mismatch detected (null if match or unavailable).</param>
public record BindingValidationResult(
    bool IsValid,
    BindingStrength Strength,
    IssueSeverity Severity,
    string? Message,
    string? SuggestedDisplay);
