// <copyright file="ValidationRules.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Sparky.Validation;

/// <summary>
/// Cached validation rules for a specific resource type.
/// Built once per resource type from IStructureDefinitionSummaryProvider metadata.
/// </summary>
public sealed record ValidationRuleSet
{
    /// <summary>
    /// Gets the collection of required element rules.
    /// </summary>
    public required IReadOnlyList<RequiredElementRule> RequiredElements { get; init; }

    /// <summary>
    /// Gets the collection of cardinality rules.
    /// </summary>
    public required IReadOnlyList<CardinalityRule> CardinalityRules { get; init; }

    /// <summary>
    /// Gets the collection of type rules.
    /// </summary>
    public required IReadOnlyList<TypeRule> TypeRules { get; init; }

    /// <summary>
    /// Gets the collection of reference field paths.
    /// </summary>
    public required IReadOnlyList<string> ReferenceFields { get; init; }

    /// <summary>
    /// Gets the collection of reference target rules (leveraging Phase 4 metadata).
    /// </summary>
    public required IReadOnlyList<ReferenceTargetRule> ReferenceTargetRules { get; init; }

    /// <summary>
    /// Gets the collection of primitive format rules.
    /// </summary>
    public required IReadOnlyList<PrimitiveFormatRule> PrimitiveFormatRules { get; init; }

    /// <summary>
    /// Gets the collection of coding field paths (CodeableConcept, Coding elements).
    /// </summary>
    public required IReadOnlyList<string> CodingFields { get; init; }

    /// <summary>
    /// Gets the collection of choice type rules.
    /// </summary>
    public required IReadOnlyList<ChoiceTypeRule> ChoiceTypeRules { get; init; }
}

/// <summary>
/// Rule for a required element (Min > 0).
/// </summary>
/// <param name="Path">The element path (e.g., "name", "identifier").</param>
public sealed record RequiredElementRule(string Path);

/// <summary>
/// Rule for element cardinality (Min/Max constraints).
/// </summary>
/// <param name="Path">The element path.</param>
/// <param name="Min">Minimum occurrences required.</param>
/// <param name="Max">Maximum occurrences allowed (null for unbounded).</param>
public sealed record CardinalityRule(string Path, int Min, int? Max);

/// <summary>
/// Rule for element data types.
/// </summary>
/// <param name="Path">The element path.</param>
/// <param name="AllowedTypes">Array of allowed type names.</param>
/// <param name="IsChoiceType">Whether this is a choice element (e.g., value[x]).</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array is appropriate for readonly metadata")]
public sealed record TypeRule(string Path, string[] AllowedTypes, bool IsChoiceType);

/// <summary>
/// Rule for reference target validation (using Phase 4 ReferenceTargets metadata).
/// </summary>
/// <param name="Path">The element path.</param>
/// <param name="AllowedTargets">Array of allowed target resource types.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array is appropriate for readonly metadata")]
public sealed record ReferenceTargetRule(string Path, string[] AllowedTargets);

/// <summary>
/// Rule for primitive type format validation.
/// </summary>
/// <param name="Path">The element path.</param>
/// <param name="PrimitiveType">The primitive type name (e.g., "id", "date", "dateTime").</param>
public sealed record PrimitiveFormatRule(string Path, string PrimitiveType);

/// <summary>
/// Rule for choice type validation (e.g., value[x] can be valueString, valueQuantity, etc.).
/// </summary>
/// <param name="Path">The element path (without [x] suffix).</param>
/// <param name="AllowedTypes">Array of allowed type names for the choice.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array is appropriate for readonly metadata")]
public sealed record ChoiceTypeRule(string Path, string[] AllowedTypes);
