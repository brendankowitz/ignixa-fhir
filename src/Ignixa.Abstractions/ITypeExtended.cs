// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Extended type metadata interface for FHIRPath evaluation, validation, and CapabilityStatement generation.
/// </summary>
/// <remarks>
/// ANALYSIS OF EXTENDED METADATA USAGE IN IGNIXA:
///
/// This interface extends <see cref="IType"/> with metadata properties extracted from FHIR StructureDefinitions
/// that are required for three core subsystems:
///
/// <para><strong>1. VALIDATION (src/Ignixa.Validation/**):</strong></para>
/// <list type="bullet">
/// <item>Cardinality checks (Min/Max)</item>
/// <item>Fixed/Pattern value validation (FixedValue, PatternValue)</item>
/// <item>Terminology binding validation (Binding)</item>
/// <item>FHIRPath invariant constraints (Constraints)</item>
/// <item>Reference target validation (ReferenceTargets)</item>
/// </list>
///
/// <para><strong>2. FHIRPATH EVALUATION (src/Ignixa.FhirPath/**):</strong></para>
/// <list type="bullet">
/// <item>Type resolution for choice elements (Types - multiple types for value[x])</item>
/// <item>Default type selection (DefaultTypeName)</item>
/// <item>Navigation for BackboneElements and recursive structures (ContentReference)</item>
/// </list>
///
/// <para><strong>3. CAPABILITYSTATEMENT GENERATION (src/Ignixa.Api/Endpoints/MetadataEndpoints.cs):</strong></para>
/// <list type="bullet">
/// <item>Currently uses hardcoded resource types, but future enhancement requires:</item>
/// <item>ReferenceTargets for search parameter target types</item>
/// <item>Binding metadata for code search parameters</item>
/// </list>
///
/// <para><strong>CATEGORIZATION:</strong></para>
///
/// <para><strong>CRITICAL</strong> (Required for core functionality - MUST be in ITypeExtended):</para>
/// <list type="bullet">
/// <item>Min/Max → Validation Tier 1 (Fast) - cardinality checks</item>
/// <item>Constraints → Validation Tier 2 (Spec) - FHIRPath invariants</item>
/// <item>Binding → Validation Tier 3 (Profile) - terminology validation</item>
/// <item>Types → FHIRPath - choice element type resolution</item>
/// <item>DefaultTypeName → FHIRPath - default type for logical models</item>
/// <item>FixedValue → Validation Tier 2 (Spec) - profile conformance</item>
/// <item>PatternValue → Validation Tier 2 (Spec) - profile conformance</item>
/// </list>
///
/// <para><strong>IMPORTANT</strong> (Used but could be lazy-loaded):</para>
/// <list type="bullet">
/// <item>ReferenceTargets → Validation Tier 2 (Spec) - reference target validation</item>
/// <item>ContentReference → FHIRPath - recursive structure navigation</item>
/// </list>
///
/// <para><strong>COMPATIBILITY NOTE:</strong></para>
/// This interface mirrors IExtendedElementMetadata from Ignixa.Specification but uses
/// Core-compatible types. Migration strategy:
/// <list type="number">
/// <item>Codegen generates both old (IElementDefinitionSummary + IExtendedElementMetadata)
/// and new (IType + ITypeExtended) simultaneously</item>
/// <item>Gradually migrate subsystems to use IType/ITypeExtended</item>
/// <item>Remove old interfaces when migration complete</item>
/// </list>
/// </remarks>
public interface ITypeExtended : IType
{
    // ===== CRITICAL PROPERTIES (Required for core functionality) =====

    /// <summary>
    /// Minimum cardinality (0 or greater).
    /// Used for validation Tier 1 - cardinality checks.
    /// </summary>
    /// <remarks>
    /// Corresponds to ElementDefinition.min from FHIR StructureDefinition.
    /// Note: <see cref="IType.IsRequired"/> is a convenience property (Min > 0).
    /// </remarks>
    int Min { get; }

    /// <summary>
    /// Maximum cardinality ("1", "5", "*" for unbounded).
    /// Used for validation Tier 1 - cardinality checks.
    /// </summary>
    /// <remarks>
    /// Corresponds to ElementDefinition.max from FHIR StructureDefinition.
    /// Note: <see cref="IType.IsCollection"/> is a convenience property (Max != "1").
    /// Common values: "0", "1", "*"
    /// </remarks>
    string Max { get; }

    /// <summary>
    /// FHIRPath constraints (invariants) for this element.
    /// Used for validation Tier 2 - FHIRPath invariant checks.
    /// </summary>
    /// <remarks>
    /// Corresponds to ElementDefinition.constraint from FHIR StructureDefinition.
    /// Examples: "dom-2: If the resource is contained in another resource, it SHALL NOT contain nested Resources"
    /// </remarks>
    IReadOnlyList<IConstraint> Constraints { get; }

    /// <summary>
    /// Terminology binding for coded elements.
    /// Used for validation Tier 3 - terminology validation.
    /// </summary>
    /// <remarks>
    /// Corresponds to ElementDefinition.binding from FHIR StructureDefinition.
    /// Null for non-coded elements.
    /// </remarks>
    IBinding? Binding { get; }

    /// <summary>
    /// Type references for this element (from ElementDefinition.type array).
    /// Used for FHIRPath type resolution, especially for choice elements (value[x]).
    /// </summary>
    /// <remarks>
    /// Corresponds to ElementDefinition.type from FHIR StructureDefinition.
    /// For choice elements (e.g., Observation.value[x]), this contains multiple type references.
    /// For simple elements, this contains a single type reference.
    /// Empty for BackboneElements and complex types defined inline.
    /// </remarks>
    IReadOnlyList<ITypeReference> Types { get; }

    /// <summary>
    /// Default type name for logical models with choice elements using typeAttr representation.
    /// Used for FHIRPath default type selection.
    /// </summary>
    /// <remarks>
    /// Corresponds to elementdefinition-defaulttype extension from FHIR StructureDefinition.
    /// Null in most cases - only populated for specialized logical models.
    /// Example: Extension-defined choice element with a default type.
    /// </remarks>
    string? DefaultTypeName { get; }

    /// <summary>
    /// Fixed value constraint for this element.
    /// Used for validation Tier 2 - profile conformance.
    /// </summary>
    /// <remarks>
    /// Corresponds to ElementDefinition.fixed[x] from FHIR StructureDefinition.
    /// When present, the element value MUST exactly match this value.
    /// Null if no fixed value constraint.
    /// Type depends on element type (e.g., string, int, CodeableConcept).
    /// </remarks>
    object? FixedValue { get; }

    /// <summary>
    /// Pattern value constraint for this element.
    /// Used for validation Tier 2 - profile conformance.
    /// </summary>
    /// <remarks>
    /// Corresponds to ElementDefinition.pattern[x] from FHIR StructureDefinition.
    /// When present, the element value MUST match this pattern (partial match).
    /// Null if no pattern constraint.
    /// Type depends on element type (e.g., string, int, CodeableConcept).
    /// Less strict than FixedValue - allows additional properties for complex types.
    /// </remarks>
    object? PatternValue { get; }

    // ===== IMPORTANT PROPERTIES (Used but could be lazy-loaded) =====

    /// <summary>
    /// Target resource types for Reference elements.
    /// Used for validation Tier 2 - reference target validation.
    /// </summary>
    /// <remarks>
    /// Extracted from ElementDefinition.type.targetProfile from FHIR StructureDefinition.
    /// Empty collection for non-Reference elements.
    /// Examples: ["Patient", "Practitioner"] for references that can point to either.
    /// Note: This is a convenience property - same information available in Types property.
    /// </remarks>
    IReadOnlyList<string> ReferenceTargets { get; }

    /// <summary>
    /// Content reference path for recursive structures.
    /// Used for FHIRPath navigation in recursive BackboneElements.
    /// </summary>
    /// <remarks>
    /// Corresponds to ElementDefinition.contentReference from FHIR StructureDefinition.
    /// Example: "#Questionnaire.item" for recursive Questionnaire.item.item
    /// Null for non-recursive elements.
    /// Format: "#" + element path
    /// </remarks>
    string? ContentReference { get; }
}
