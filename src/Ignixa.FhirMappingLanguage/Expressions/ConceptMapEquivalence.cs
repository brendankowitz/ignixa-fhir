/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Equivalence relationship for ConceptMap code mappings.
/// </summary>
public enum ConceptMapEquivalence
{
    /// <summary>Equivalent (==)</summary>
    Equivalent,
    /// <summary>Related to (~=)</summary>
    RelatedTo,
    /// <summary>Not related to (!=)</summary>
    NotRelatedTo,
    /// <summary>Broader (&lt;-)</summary>
    Broader,
    /// <summary>Narrower (-&gt;)</summary>
    Narrower
}
