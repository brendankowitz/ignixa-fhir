// <copyright file="IValidationCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Base interface for all validation checks.
/// Checks implement specific validation logic (cardinality, type checking, invariants, etc.).
/// Uses IElement for FHIR-aware navigation (choice types, shadow properties).
/// </summary>
public interface IValidationCheck
{
    /// <summary>
    /// Validates a FHIR element against this check's rules.
    /// </summary>
    /// <param name="element">The element to validate (FHIR-aware navigation).</param>
    /// <param name="settings">Validation settings and configuration.</param>
    /// <param name="state">Current validation state (Global/Instance/Location context).</param>
    /// <returns>Validation result with any issues found.</returns>
    ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state);
}
