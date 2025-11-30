// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Type metadata from FHIR StructureDefinition.
/// Provides design-time type information for validation and serialization.
/// </summary>
/// <remarks>
/// This interface replaces the legacy IElementDefinitionSummary interface.
/// It provides strongly-typed metadata via the <see cref="Info"/> property and uses
/// <see cref="IReadOnlyList{T}"/> for safe, efficient access to child types.
/// </remarks>
public interface IType
{
    /// <summary>
    /// Strongly-typed type information (struct for stack allocation).
    /// Provides fast access to core type metadata without heap allocations.
    /// </summary>
    TypeInfo Info { get; }

    /// <summary>
    /// Maximum cardinality > 1 (array/list type).
    /// Required for validation (Tier 2).
    /// </summary>
    bool IsCollection { get; }

    /// <summary>
    /// Minimum cardinality > 0 (required element).
    /// Required for validation (Tier 2).
    /// </summary>
    bool IsRequired { get; }

    /// <summary>
    /// Included in _summary=true responses.
    /// Required for FHIR search _summary parameter support.
    /// </summary>
    bool InSummary { get; }

    /// <summary>
    /// Element serialization order (from StructureDefinition snapshot array position).
    /// REQUIRED for XML serialization (FHIR spec: "Elements must appear in documented order").
    /// NOT used for canonical JSON (alphabetical property name order instead).
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Child type definitions (empty for primitives).
    /// Returns an immutable, indexable collection for safe iteration.
    /// </summary>
    IReadOnlyList<IType> Children { get; }
}
