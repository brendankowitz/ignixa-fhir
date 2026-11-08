// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates a complex <c>value[x]</c> choice variant (e.g. <c>valueAttachment</c>) against its
/// datatype schema, so structural rules that live inside the variant — such as the base64Binary
/// encoding of <c>Attachment.data</c> — are enforced. <see cref="ChoiceElementCheck"/> only recurses
/// into primitive variants, leaving complex-variant subtrees dark; this closes that gap.
/// </summary>
/// <remarks>
/// Registered in the Spec tier, as <see cref="NestedComplexTypeCheck"/> is, so the variant subtree is
/// validated at every depth a non-polymorphic element's subtree is (Spec, Full and Compatibility), at
/// the caller's depth. An element's type, not whether it is a choice, decides how deeply it is checked. A datatype's
/// invariants do not become optional because the element carrying it happens to be a choice:
/// <c>Dosage.timing</c> and <c>ServiceRequest.occurrence[x]</c> both hold a <c>Timing</c>, and tim-9
/// binds to <c>Timing.repeat</c> in both.
/// <para>
/// This check once ran only at <see cref="ValidationDepth.Full"/>, leaving complex variants unchecked at Spec while a
/// non-choice element of the same type was checked. Before that it ran the subtree at <see cref="ValidationDepth.Spec"/> to keep FHIRPath invariants
/// dark, because R4's tim-9 is ill-formed for a repeating <c>Timing.repeat.when</c> and the engine's
/// refusal to evaluate it surfaced as a resource error on conformant data. That is now fixed at the
/// source: <see cref="FhirPathInvariantCheck"/> routes <c>FhirPathEvaluationException</c> to a
/// non-failing Warning, so an unevaluable constraint can no longer reject a resource and the
/// demotion is no longer buying anything except a hole.
/// </para>
/// </remarks>
public sealed class ChoiceVariantNestedCheck : IValidationCheck
{
    private readonly string _variantName;
    private readonly string _variantShadowName;
    private readonly bool _isCollection;
    private readonly ValidationSchema _variantSchema;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChoiceVariantNestedCheck"/> class.
    /// </summary>
    /// <param name="variantName">The concrete variant element name (e.g. "valueAttachment").</param>
    /// <param name="isCollection">Whether the choice element is a collection.</param>
    /// <param name="variantSchema">The datatype schema for the variant.</param>
    public ChoiceVariantNestedCheck(string variantName, bool isCollection, ValidationSchema variantSchema)
    {
        _variantName = variantName ?? throw new ArgumentNullException(nameof(variantName));
        _variantShadowName = $"_{variantName}";
        _isCollection = isCollection;
        _variantSchema = variantSchema ?? throw new ArgumentNullException(nameof(variantSchema));
    }

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        // Registered in the spec tier, which ValidationSchema does not run at Minimal; the guard keeps
        // a direct caller to the same contract.
        if (settings.Depth == ValidationDepth.Minimal)
        {
            return ValidationResult.Success();
        }

        // One check per variant runs on every node carrying the choice element, and at most one variant is present.
        if (RawMembers.CertainlyAbsent(element, _variantName, _variantShadowName))
        {
            return ValidationResult.Success();
        }

        var variantNodes = element.Children(_variantName);
        if (variantNodes.Count == 0)
        {
            return ValidationResult.Success();
        }

        if (!state.TryDescend(out var descended))
        {
            return new ValidationResult(
                isValid: true,
                issues: new[] { ValidationIssue.NestingLimitExceeded(state.Location.InstancePath, _variantName) });
        }

        // Issues propagate regardless of the nested result's validity: gating on !IsValid would
        // drop every non-failing Warning raised inside the variant, including the engine-refusal
        // warnings that exist to be reported without failing the resource.
        var issues = new List<ValidationIssue>();
        var isValid = true;
        for (var i = 0; i < variantNodes.Count; i++)
        {
            var location = _isCollection ? $"{_variantName}[{i}]" : _variantName;
            var nestedState = descended.WithLocation(location);
            var nestedResult = _variantSchema.Validate(variantNodes[i], settings, nestedState);
            issues.AddRange(nestedResult.IssuesOrSynthesizedFailure(
                variantNodes[i].Location, $"'{location}' ({_variantSchema.ResourceType})"));
            isValid &= nestedResult.IsValid;
        }

        return new ValidationResult(isValid, issues);
    }
}
