// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Terminology;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// The rule names a validation summary uses for checks that did not run to a conclusion. Any one of these on a resource
/// without errors makes its verdict <c>unknown</c>, never <c>valid</c>.
/// </summary>
internal static class ValidationRules
{
    /// <summary>A <c>meta.profile</c> (or the requested profile) whose StructureDefinition is not loaded.</summary>
    public const string ProfileNotLoaded = "profile-not-loaded";

    /// <summary>A FHIRPath constraint the engine could not evaluate against the resource.</summary>
    public const string ConstraintNotEvaluated = "constraint-not-evaluated";

    /// <summary>The terminology adapter failed unexpectedly; the binding was not checked.</summary>
    public const string TerminologyError = "terminology-error";

    /// <summary>A slicing whose discriminators could not be evaluated, so it was not enforced.</summary>
    public const string SlicingDeferred = "slicing-deferred";

    /// <summary>A subtree below the validator's nesting limit, left unvalidated.</summary>
    public const string NestingLimit = "validation-nesting-limit";

    /// <summary>The resource's type has no validation schema.</summary>
    public const string SchemaNotFound = "schema-not-found";

    /// <summary>A resource claims a profile that constrains another resource type.</summary>
    public const string ProfileTypeMismatch = "profile-type-mismatch";

    /// <summary>
    /// Validated at <c>spec</c> depth, which does not run invariants, slicing, extensible bindings or complex choice variants.
    /// Every resource is subject to invariants (<c>ele-1</c> on every element, <c>dom-*</c> on every DomainResource), so at
    /// <c>spec</c> one of the skipped checks always applies and no resource can be shown valid.
    /// </summary>
    public const string DepthSpec = "depth-spec";

    /// <summary>The resource has extensions, which Ignixa.Validation does not validate (see <see cref="ValidatorGapDetectors"/>).</summary>
    public const string ExtensionsNotValidated = "extensions-not-validated";

    /// <summary>A reference to a resource type the element does not allow (see <see cref="ValidatorGapDetectors"/>).</summary>
    public const string ReferenceTargetType = "reference-target-type";

    /// <summary>A failed FHIRPath invariant; the group names its key in <c>constraint</c>.</summary>
    public const string Invariant = "invariant";

    /// <summary>The rules that leave a verdict unknown.</summary>
    public static IReadOnlySet<string> Unevaluated { get; } = new HashSet<string>(
        [StoreTerminologyService.NotCheckedRule, ProfileNotLoaded, ConstraintNotEvaluated, TerminologyError, SlicingDeferred, NestingLimit, SchemaNotFound, DepthSpec, ExtensionsNotValidated],
        StringComparer.Ordinal);

    /// <summary>The rules counted in a summary's <c>notChecked</c>: the unevaluated ones and unchecked extensible bindings.</summary>
    public static IReadOnlySet<string> NotChecked { get; } = new HashSet<string>(
        [.. Unevaluated, StoreTerminologyService.ExtensibleNotCheckedRule],
        StringComparer.Ordinal);
}
