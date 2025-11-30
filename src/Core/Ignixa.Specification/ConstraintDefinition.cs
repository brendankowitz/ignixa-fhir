// <copyright file="ConstraintDefinition.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

namespace Ignixa.Specification;

/// <summary>
/// Represents a FHIR constraint definition (invariant).
/// </summary>
public sealed record ConstraintDefinition
{
    /// <summary>
    /// Gets the constraint key (e.g., "pat-1", "dom-1", "ele-1").
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the constraint severity level (Error or Warning).
    /// </summary>
    public required ConstraintSeverity Severity { get; init; }

    /// <summary>
    /// Gets the human-readable description of the constraint.
    /// </summary>
    public required string Human { get; init; }

    /// <summary>
    /// Gets the FHIRPath expression to evaluate.
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    /// Gets the optional XPath expression (legacy, rarely used).
    /// </summary>
    public string? Xpath { get; init; }

    /// <summary>
    /// Gets the list of resource/datatype names this constraint applies to.
    /// Example: ["Patient"], ["Observation"], ["Element"] (applies to all).
    /// </summary>
    public required IReadOnlyList<string> AppliesTo { get; init; }
}
