// <copyright file="IExtendedElementMetadata.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Specification;

/// <summary>
/// Extended metadata interface for accessing rich FHIR element metadata.
/// This interface provides access to metadata extracted from FHIR StructureDefinitions
/// that goes beyond the basic IElementDefinitionSummary interface.
/// </summary>
/// <remarks>
/// Cast IElementDefinitionSummary to this interface to access extended metadata:
/// <code>
/// if (elementSummary is IExtendedElementMetadata extended)
/// {
///     var targets = extended.ReferenceTargets;
///     var binding = extended.Binding;
/// }
/// </code>
/// </remarks>
public interface IExtendedElementMetadata
{
    /// <summary>
    /// Gets the allowed reference target resource types for Reference elements.
    /// Null if not a Reference element or no target profiles specified.
    /// </summary>
    /// <example>
    /// For Observation.subject: ["Patient", "Device", "Practitioner", "Location"]
    /// </example>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array is appropriate for readonly metadata")]
    string[]? ReferenceTargets { get; }

    /// <summary>
    /// Gets the ValueSet binding information for coded elements.
    /// Null if no binding specified.
    /// </summary>
    BindingMetadata? Binding { get; }

    /// <summary>
    /// Gets the FHIRPath constraints (invariants) for this element.
    /// Empty array if no constraints specified.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array is appropriate for readonly metadata")]
    ConstraintDefinition[]? Constraints { get; }

    /// <summary>
    /// Gets the slicing information for elements that can be sliced.
    /// Null if no slicing defined.
    /// </summary>
    SlicingMetadata? Slicing { get; }

    /// <summary>
    /// Gets the fixed value for this element (serialized as JSON).
    /// Null if no fixed value specified.
    /// </summary>
    string? FixedValue { get; }

    /// <summary>
    /// Gets the pattern value for this element (serialized as JSON).
    /// Null if no pattern value specified.
    /// </summary>
    string? PatternValue { get; }

    /// <summary>
    /// Gets the default value for this element (serialized as JSON).
    /// Null if no default value specified.
    /// </summary>
    string? DefaultValue { get; }

    /// <summary>
    /// Gets the content reference for recursive structures.
    /// Example: "#Patient.contact" for Patient.contact.contact element.
    /// Null if no content reference specified.
    /// </summary>
    string? ContentReference { get; }

    /// <summary>
    /// Gets the minimum cardinality for this element.
    /// Null if using default from base definition.
    /// </summary>
    int? Min { get; }

    /// <summary>
    /// Gets the maximum cardinality for this element.
    /// "*" for unbounded, numeric string for specific max.
    /// Null if using default from base definition.
    /// </summary>
    string? Max { get; }
}

/// <summary>
/// ValueSet binding metadata for coded elements.
/// </summary>
/// <param name="ValueSetUrl">The canonical URL of the ValueSet (may include version suffix).</param>
/// <param name="Strength">The binding strength: Required, Extensible, Preferred, or Example.</param>
public record BindingMetadata(string ValueSetUrl, string Strength);

/// <summary>
/// Slicing metadata for elements that can be sliced in profiles.
/// </summary>
/// <param name="Discriminators">Array of discriminator paths (e.g., ["type:url"] for extension slicing).</param>
/// <param name="Rules">Slicing rules: Open, Closed, or OpenAtEnd.</param>
/// <param name="Ordered">Whether slices must appear in a specific order.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array is appropriate for readonly metadata")]
public record SlicingMetadata(string[] Discriminators, string Rules, bool Ordered);
