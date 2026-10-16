// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using Ignixa.CliOnFhir.Terminology;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// The tool's own one-line description of each rule a validation summary names: the issue codes Ignixa.Validation emits, the
/// terminology adapter's and the tool's rules, and the core FHIR invariants that fail most often. A summary never shows the
/// validator's message (it quotes the data) or a profile's constraint text (it is profile content); a rule not listed here
/// gets no description, only its code and path.
/// </summary>
internal static class RuleDescriptions
{
    private const string GenericInvariant = "A FHIRPath invariant (named in 'constraint') of the profile is not satisfied.";

    private static readonly FrozenDictionary<string, string> Rules = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["cardinality-violation"] = "The element occurs fewer times than its minimum, or more times than its maximum, cardinality.",
        ["type-1"] = "The value does not have the element's FHIR type: a wrong JSON type, or a primitive value that is not valid for its type.",
        ["structure-1"] = "The JSON is not valid FHIR JSON structure at this element.",
        ["structure-array"] = "The element repeats, so it must be a JSON array.",
        ["structure-null"] = "The element is JSON null, which FHIR JSON does not allow.",
        ["resourcetype-1"] = "The resourceType is missing or not a resource type of this FHIR version.",
        ["unknown-property"] = "The element is not defined for this type; its name, chosen by the data, is shown as a digest.",
        ["choice-multiple"] = "A choice element ([x]) has more than one type variant.",
        ["choice-missing-type"] = "A choice element ([x]) is written without a type suffix.",
        ["choice-invalid-type"] = "A choice element ([x]) uses a type it does not allow.",
        ["pattern-mismatch"] = "The element does not match the pattern its profile requires.",
        ["fixed-value-mismatch"] = "The element does not have the fixed value its profile requires.",
        ["slicing-unmatched"] = "A repetition matches no slice of a closed slicing, or comes before the matched slices of an openAtEnd slicing.",
        ["slicing-out-of-order"] = "A repetition matches a slice out of order in an ordered slicing.",
        ["slicing-cardinality"] = "A slice occurs fewer or more times than its cardinality.",
        ["coding-1"] = "A Coding or CodeableConcept has neither a system nor a code.",
        ["coding-system-absolute"] = "A Coding's system is not an absolute URI.",
        ["invalid-code"] = "A code is not valid in its code system.",
        ["id-1"] = "The id is not a valid FHIR id.",
        ["ref-1"] = "A reference is neither a relative reference, an absolute URL, a fragment nor a urn.",
        ["ref-resolve"] = "A reference does not resolve.",
        ["contained-missing-resourcetype"] = "A contained resource has no resourceType.",
        ["contained-invalid-resourcetype"] = "A contained resource's resourceType is not a resource type of this FHIR version.",
        ["ext-url-required"] = "An extension has no url.",
        ["ext-content-required"] = "An extension has neither a value nor nested extensions.",
        ["ext-both-value-and-nested"] = "An extension has both a value and nested extensions.",
        ["ext-url-version"] = "An extension url carries a version.",
        ["ext-example-url"] = "An extension url is an example URL.",
        ["validation-nested-unexplained"] = "A nested element failed validation without a reported reason.",
        ["validation-error"] = "The validator reported an error at this element.",
        [StoreTerminologyService.NotInValueSetRule] = "The code is not in the ValueSet of a required binding (named in 'valueSet').",
        [StoreTerminologyService.NotInExtensibleValueSetRule] = "The code is not in the ValueSet of an extensible binding (named in 'valueSet'); allowed only when no suitable code exists there.",
        [StoreTerminologyService.NotCheckedRule] = "A required binding was not checked: its ValueSet or code system is not loaded, its expansion is incomplete, the concept has text only, or the terminology server could not decide it.",
        [StoreTerminologyService.ExtensibleNotCheckedRule] = "An extensible binding was not checked with the loaded terminology; it cannot make the resource invalid.",
        [ValidationRules.TerminologyError] = "The terminology adapter failed, so the binding was not checked.",
        [ValidationRules.ProfileNotLoaded] = "The resource claims a profile (named in 'profile') that is not loaded, so it was validated against the core definition only.",
        [ValidationRules.ProfileTypeMismatch] = "The resource claims a profile (named in 'profile') of another resource type.",
        [ValidationRules.SchemaNotFound] = "No validation schema is available for this resource type.",
        [ValidationRules.ConstraintNotEvaluated] = "A FHIRPath constraint (named in 'constraint') could not be evaluated against the resource.",
        [ValidationRules.SlicingDeferred] = "A slicing could not be evaluated (its discriminator could not be read), so it was not enforced.",
        [ValidationRules.NestingLimit] = "The element is nested deeper than the validator descends, so it was not validated.",
        [ValidationRules.DepthSpec] = "Validated at depth spec, which does not run invariants, slicing, extensible bindings or extension definitions; validate at depth full for a verdict.",
        [ValidationRules.ReferenceTypeMismatch] = "A reference's 'type' names a different resource type than its 'reference' does.",
        [ValidationRules.ReferenceTargetType] = "A reference points at a resource type the element does not allow (by the core definition or a profile's target profiles).",
        [ValidationRules.ExtensionDefinitionUnchecked] = "An extension was validated only as the Extension datatype: its definition is not loaded, or it is a sub-extension whose slice rules are not checked; so the verdict is unknown.",
        [ValidationRules.BindingRequiredTextOnly] = "A CodeableConcept under a required binding has text only: it carries no coding from the ValueSet (named in 'valueSet').",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> CoreInvariants = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ele-1"] = "Every element must have a value or children.",
        ["ext-1"] = "An extension must have either nested extensions or a value, not both.",
        ["dom-2"] = "A contained resource must not contain other resources.",
        ["dom-3"] = "A contained resource must be referenced from elsewhere in the resource, or reference the resource.",
        ["dom-4"] = "A contained resource must not have meta.versionId or meta.lastUpdated.",
        ["dom-5"] = "A contained resource must not have security labels.",
        ["dom-6"] = "A resource should have a narrative (text) for robust management.",
        ["txt-1"] = "A narrative must contain only basic HTML formatting.",
        ["txt-2"] = "A narrative must have some non-whitespace content.",
        ["bdl-7"] = "Each Bundle entry's fullUrl must be unique.",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="code"/> is one of the rules this table describes (so it is not a constraint key).</summary>
    public static bool IsKnownRule(string code) => Rules.ContainsKey(code);

    /// <summary>
    /// The description of <paramref name="rule"/>; for an invariant, its core meaning only when the StructureDefinition that
    /// raised it (<paramref name="profile"/>) is a core one, since a profile may define a key of the same name; null for a rule
    /// not listed.
    /// </summary>
    public static string? Describe(string rule, string? constraint, string profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (rule != ValidationRules.Invariant)
        {
            return Rules.GetValueOrDefault(rule);
        }

        bool core = profile.StartsWith("http://hl7.org/fhir/StructureDefinition/", StringComparison.Ordinal);
        return core && constraint is not null && CoreInvariants.TryGetValue(constraint, out string? known) ? known : GenericInvariant;
    }
}
