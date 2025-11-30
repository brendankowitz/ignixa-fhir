/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.TypeSystem;

/// <summary>
/// Category of FHIR type.
/// </summary>
public enum TypeCategory
{
    /// <summary>
    /// Primitive type (string, integer, decimal, boolean, etc.)
    /// </summary>
    Primitive,

    /// <summary>
    /// Complex data type (HumanName, Address, CodeableConcept, etc.)
    /// </summary>
    Complex,

    /// <summary>
    /// Resource type (Patient, Observation, etc.)
    /// </summary>
    Resource,

    /// <summary>
    /// Unknown or unresolved type
    /// </summary>
    Unknown
}
