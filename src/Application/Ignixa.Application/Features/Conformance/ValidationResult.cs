// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Result of validation checks during package activation.
/// </summary>
public record ValidationResult(bool Success, IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>
    /// Creates a validation result indicating no issues were found.
    /// </summary>
    public static ValidationResult Valid() => new(true, []);

    /// <summary>
    /// Creates a validation result with one or more issues.
    /// </summary>
    public static ValidationResult Invalid(IEnumerable<ValidationIssue> issues) => new(false, issues.ToList());
}
