// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation;

/// <summary>
/// The message templates the validator's checks render issues from (<see cref="ValidationIssue.TemplateId"/>). An issue built
/// from free text (a custom check, or a check not yet converted) has no template. Ids are stable; see <see cref="IssueTemplate"/>.
/// </summary>
public static class IssueTemplates
{
    /// <summary>A failed invariant: the constraint key, then its description (a definition's <c>human</c> text, or the check's).</summary>
    public static IssueTemplate Invariant { get; } = new("invariant", "{0}: {1}", 2);

    /// <summary>A constraint whose FHIRPath expression failed to parse: the constraint key.</summary>
    public static IssueTemplate InvariantNotParsed { get; } = new("invariant-not-parsed", "Constraint '{0}' could not be evaluated: FHIRPath expression failed to parse", 1);

    /// <summary>A constraint the engine could not evaluate: the constraint key, then the engine's message.</summary>
    public static IssueTemplate InvariantNotEvaluated { get; } = new("invariant-not-evaluated", "Constraint '{0}' could not be evaluated: {1}", 2);

    /// <summary>An unexpected error evaluating a constraint: the constraint key, then the error's message.</summary>
    public static IssueTemplate InvariantEvaluationError { get; } = new("invariant-evaluation-error", "{0}: unexpected error evaluating FHIRPath expression: {1}", 2);

    /// <summary>A subtree left unvalidated at the nesting limit: the element name, then the limit.</summary>
    public static IssueTemplate NestingLimit { get; } = new("validation-nesting-limit", "'{0}' was not validated: nesting depth limit of {1} reached", 2);

    /// <summary>A nested result that was invalid without saying why: what was validated.</summary>
    public static IssueTemplate NestedUnexplained { get; } = new("validation-nested-unexplained", "{0} failed validation but reported no issues explaining why", 1);

    /// <summary>A code not in a ValueSet: the system, the code, then the ValueSet canonical.</summary>
    public static IssueTemplate CodeNotInValueSet { get; } = new("code-not-in-value-set", "The provided code '{0}#{1}' was not found in the value set '{2}'", 3);

    /// <summary>A code unknown in its code system: the code, then the system.</summary>
    public static IssueTemplate CodeUnknownInSystem { get; } = new("code-unknown-in-system", "Unknown code '{0}' in the CodeSystem '{1}'", 2);

    /// <summary>Too few occurrences: the element path, the minimum, then the count found.</summary>
    public static IssueTemplate CardinalityBelowMinimum { get; } = new("cardinality-below-minimum", "{0} must have at least {1} occurrence(s), but found {2}", 3);

    /// <summary>Too many occurrences: the element path, the maximum, then the count found.</summary>
    public static IssueTemplate CardinalityAboveMaximum { get; } = new("cardinality-above-maximum", "{0} must have at most {1} occurrence(s), but found {2}", 3);

    /// <summary>A cardinality violation stated as a range: the element path, the minimum, the maximum, then the count found.</summary>
    public static IssueTemplate CardinalityViolation { get; } = new("cardinality-violation", "{0} cardinality violation: expected {1}..{2}, found {3}", 4);

    /// <summary>A primitive whose JSON kind is wrong: the expected JSON kind, the FHIR type, then the JSON kind found.</summary>
    public static IssueTemplate TypeJsonKind { get; } = new("type-json-kind", "type-1: JSON type mismatch: expected {0} for FHIR type '{1}', but got {2}", 3);

    /// <summary>A string-valued primitive over the 1 MB limit: the limit in bytes.</summary>
    public static IssueTemplate TypeTooLong { get; } = new("type-too-long", "type-1: Value is longer than permitted maximum length of 1 MB ({0} bytes)", 1);

    /// <summary>A primitive value not valid for its type: the value, then the type (or element) it was checked as.</summary>
    public static IssueTemplate TypeInvalidValue { get; } = new("type-invalid-value", "type-1: Value '{0}' is not a valid '{1}'", 2);

    /// <summary>A primitive value breaking a rule of its type: the reason, which may quote the value.</summary>
    public static IssueTemplate TypePrimitiveRule { get; } = new("type-primitive-rule", "type-1: {0}", 1);

    /// <summary>A choice element with several variants: its base name, then the variant names found.</summary>
    public static IssueTemplate ChoiceMultiple { get; } = new("choice-multiple", "Choice element '{0}[x]' can only have one type variant, but found multiple: {1}", 2);

    /// <summary>A choice element without a type suffix: its base name, an example type, the name found, then the allowed types.</summary>
    public static IssueTemplate ChoiceMissingType { get; } = new("choice-missing-type", "Choice element '{0}[x]' must include a type suffix (e.g., '{0}{1}'). Found '{2}' without type suffix. Allowed types: {3}", 4);

    /// <summary>A choice element of a type it does not allow: its base name, the type found, then the allowed types.</summary>
    public static IssueTemplate ChoiceInvalidType { get; } = new("choice-invalid-type", "Choice element '{0}[x]' has type '{1}' which is not in the allowed types: {2}", 3);

    /// <summary>A primitive choice variant with an invalid value: the variant name, then the reason (which may quote the value).</summary>
    public static IssueTemplate ChoicePrimitiveInvalid { get; } = new("choice-primitive-invalid", "Invalid value for '{0}': {1}", 2);

    /// <summary>A property its type does not define: the property name, then the type.</summary>
    public static IssueTemplate UnknownProperty { get; } = new("unknown-property", "Property '{0}' is not defined in the FHIR StructureDefinition for {1}", 2);

    /// <summary>A property its (unnamed) type does not define: the property name.</summary>
    public static IssueTemplate UnknownPropertyUntyped { get; } = new("unknown-property-untyped", "Property '{0}' is not defined in the FHIR StructureDefinition for this resource type", 1);

    /// <summary>A reference string that is not a reference: the reference.</summary>
    public static IssueTemplate ReferenceFormat { get; } = new("reference-format", "ref-1: Reference '{0}' is not a valid FHIR reference format", 1);

    /// <summary>A local reference that does not resolve: the reference.</summary>
    public static IssueTemplate ReferenceUnresolved { get; } = new("reference-unresolved", "ref-resolve: Local reference '{0}' does not resolve within the resource", 1);

    /// <summary>A reference to a resource type the element does not allow: the type, then the allowed types.</summary>
    public static IssueTemplate ReferenceTargetType { get; } = new("reference-target-type", "The reference is to a resource of type {0}, which this element does not allow ({1})", 2);

    /// <summary>A reference whose <c>type</c> names another type than its <c>reference</c>: the reference's type, then <c>type</c>'s.</summary>
    public static IssueTemplate ReferenceTypeMismatch { get; } = new("reference-type-mismatch", "The reference is to a resource of type {0}, but its type says {1}", 2);

    /// <summary>A reference whose target type cannot be told (an unresolved urn, an identifier only): the allowed types.</summary>
    public static IssueTemplate ReferenceTargetUndeterminable { get; } = new("reference-target-undeterminable", "The referenced resource's type cannot be told from the reference, so it was not checked against the allowed types ({0})", 1);

    /// <summary>A reference with only a <c>display</c>: the allowed types.</summary>
    public static IssueTemplate ReferenceTargetDisplayOnly { get; } = new("reference-target-display-only", "The reference has only a display, so its target type was not checked against the allowed types ({0})", 1);

    /// <summary>An element whose target profile is not loaded, so what it allows cannot be told: the target profile canonical.</summary>
    public static IssueTemplate ReferenceTargetProfileNotLoaded { get; } = new("reference-target-profile-not-loaded", "The target profile '{0}' is not loaded, so the referenced resource's type was not checked", 1);

    /// <summary>A required binding on a concept with no coding: the ValueSet canonical.</summary>
    public static IssueTemplate BindingRequiredTextOnly { get; } = new("binding-required-text-only", "The concept has no coding, so it is not from the value set '{0}' its required binding names", 1);

    /// <summary>A coded value the terminology service rejected: the service's message.</summary>
    public static IssueTemplate BindingCodeInvalid { get; } = new("binding-code-invalid", "{0}", 1);

    /// <summary>A coded value outside an example binding's ValueSet: the terminology service's message.</summary>
    public static IssueTemplate BindingExampleMiss { get; } = new("binding-example-miss", "{0}", 1);

    /// <summary>A binding the terminology service could not check: the service's message.</summary>
    public static IssueTemplate BindingNotChecked { get; } = new("binding-not-checked", "{0}", 1);

    /// <summary>A terminology service failure: the exception's message.</summary>
    public static IssueTemplate BindingTerminologyError { get; } = new("binding-terminology-error", "Terminology validation failed: {0}", 1);

    /// <summary>A Coding with neither system nor code.</summary>
    public static IssueTemplate CodingWithoutSystemOrCode { get; } = new("coding-without-system-or-code", "Coding should have at least a system or code", 0);

    /// <summary>A Coding whose system is not an absolute URI.</summary>
    public static IssueTemplate CodingSystemNotAbsolute { get; } = new("coding-system-not-absolute", "coding-system-absolute: Coding.system must be an absolute reference, not a local reference", 0);

    /// <summary>A <c>meta.profile</c> canonical that is not loaded: the canonical.</summary>
    public static IssueTemplate ProfileNotResolved { get; } = new("profile-not-resolved", "Profile '{0}' declared in meta.profile could not be resolved and was not validated. Available schemas were still applied.", 1);

    /// <summary>An extension url on a reserved example domain: the url.</summary>
    public static IssueTemplate ExtensionExampleUrl { get; } = new("extension-example-url", "ext-example-url: Example URLs are not allowed in this context ({0})", 1);

    /// <summary>An extension whose definition is not loaded, validated as a generic Extension only: the extension url.</summary>
    public static IssueTemplate ExtensionDefinitionNotLoaded { get; } = new("extension-definition-not-loaded", "Extension '{0}' has no loaded definition, so it was validated as a generic Extension only", 1);

    /// <summary>
    /// A sub-extension of a complex extension whose definition is loaded, validated as a generic Extension only because the rules
    /// a definition gives its slices are not modelled: the sub-extension url.
    /// </summary>
    public static IssueTemplate ExtensionSliceNotValidated { get; } = new("extension-slice-not-validated", "Sub-extension '{0}' was validated as a generic Extension only: the rules its extension's definition gives it were not checked", 1);

    /// <summary>A Bundle or Parameters entry resource without a resourceType, or with one that is not a resource type.</summary>
    public static IssueTemplate EntryResourceTypeUnknown { get; } = new("entry-resource-type-unknown", "The resource carried here has no resourceType, or one that is not a resource type of this FHIR version", 0);

    /// <summary>An extension used where its definition's contexts do not allow it: the extension url, then the contexts.</summary>
    public static IssueTemplate ExtensionContextMismatch { get; } = new("extension-context-mismatch", "Extension '{0}' is not allowed here: its definition allows it on {1}", 2);

    /// <summary>A modifier extension's definition used under <c>extension</c>: the extension url.</summary>
    public static IssueTemplate ExtensionModifierNotInModifierExtension { get; } = new("extension-modifier-not-in-modifierextension", "Extension '{0}' is defined as a modifier extension, so it must appear in modifierExtension", 1);

    /// <summary>A non-modifier extension's definition used under <c>modifierExtension</c>: the extension url.</summary>
    public static IssueTemplate ExtensionNotModifierInModifierExtension { get; } = new("extension-not-modifier-in-modifierextension", "Extension '{0}' is not defined as a modifier extension, so it must not appear in modifierExtension", 1);

    /// <summary>An extension whose definition's context is a FHIRPath expression, which is not evaluated: the extension url.</summary>
    public static IssueTemplate ExtensionContextFhirPathUnchecked { get; } = new("extension-context-fhirpath-unchecked", "The context of extension '{0}' was not checked: its definition states it as a FHIRPath expression", 1);

    /// <summary>
    /// An extension whose element context names a type or element id the validator does not resolve (an abstract type such
    /// as <c>CanonicalResource</c>, a profile-qualified element id): the extension url, then the context.
    /// </summary>
    public static IssueTemplate ExtensionContextUnresolved { get; } = new("extension-context-unresolved", "The context of extension '{0}' was not checked: its context '{1}' names a type or element id this validator does not resolve", 2);

    /// <summary>An extension whose definition's contexts the schema does not describe: the extension url.</summary>
    public static IssueTemplate ExtensionContextNotDescribed { get; } = new("extension-context-not-described", "The context of extension '{0}' was not checked: the schema does not describe extension contexts", 1);

    /// <summary>A resource-level extension with a relative url: the url.</summary>
    public static IssueTemplate ExtensionUrlRelative { get; } = new("extension-url-relative", "Extension url '{0}' is relative; only the sub-extensions of a complex extension may use a relative url", 1);

    /// <summary>Every template above, for consumers that key a table on <see cref="IssueTemplate.Id"/>.</summary>
    public static IReadOnlyList<IssueTemplate> All { get; } =
    [
        Invariant, InvariantNotParsed, InvariantNotEvaluated, InvariantEvaluationError, NestingLimit, NestedUnexplained,
        CodeNotInValueSet, CodeUnknownInSystem, CardinalityBelowMinimum, CardinalityAboveMaximum, CardinalityViolation,
        TypeJsonKind, TypeTooLong, TypeInvalidValue, TypePrimitiveRule, ChoiceMultiple, ChoiceMissingType, ChoiceInvalidType,
        ChoicePrimitiveInvalid, UnknownProperty, UnknownPropertyUntyped, ReferenceFormat, ReferenceUnresolved, ReferenceTargetType,
        ReferenceTypeMismatch, ReferenceTargetUndeterminable, ReferenceTargetDisplayOnly, ReferenceTargetProfileNotLoaded,
        BindingRequiredTextOnly, BindingCodeInvalid, BindingExampleMiss, BindingNotChecked, BindingTerminologyError,
        CodingWithoutSystemOrCode, CodingSystemNotAbsolute, ProfileNotResolved, ExtensionExampleUrl,
        ExtensionDefinitionNotLoaded, ExtensionSliceNotValidated, EntryResourceTypeUnknown, ExtensionContextMismatch,
        ExtensionModifierNotInModifierExtension, ExtensionNotModifierInModifierExtension, ExtensionContextFhirPathUnchecked,
        ExtensionContextUnresolved, ExtensionContextNotDescribed, ExtensionUrlRelative,
    ];
}
