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
/// The nested schema is run at <see cref="ValidationDepth.Spec"/> (structural tier only), never Full.
/// A choice variant's own FHIRPath datatype invariants (tim-*, etc.) are deliberately not lit here:
/// they are not part of the base resource's obligation, and evaluating them across the many choice
/// variants suite-wide surfaces engine false positives on valid data (e.g. tim-9), which would reject
/// resources the reference validator accepts. Structural checks (cardinality, type/primitive format,
/// shape) carry no such risk and are what this fix is for.
/// </remarks>
public sealed class ChoiceVariantNestedCheck : IValidationCheck
{
    private readonly string _variantName;
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
        _isCollection = isCollection;
        _variantSchema = variantSchema ?? throw new ArgumentNullException(nameof(variantSchema));
    }

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        // Registered in the profile tier; only engage at Full depth so Compatibility/Spec runs are
        // untouched. The variant subtree itself is then validated at Spec (see remarks).
        if (settings.Depth < ValidationDepth.Full)
        {
            return ValidationResult.Success();
        }

        var variantNodes = element.Children(_variantName);
        if (variantNodes.Count == 0)
        {
            return ValidationResult.Success();
        }

        var structuralSettings = new ValidationSettings
        {
            Depth = ValidationDepth.Spec,
            SkipTerminologyValidation = settings.SkipTerminologyValidation,
            TerminologyFailureMode = settings.TerminologyFailureMode,
            TerminologyService = settings.TerminologyService,
        };

        var issues = new List<ValidationIssue>();
        for (var i = 0; i < variantNodes.Count; i++)
        {
            var location = _isCollection ? $"{_variantName}[{i}]" : _variantName;
            var nestedState = state.WithLocation(location);
            var nestedResult = _variantSchema.Validate(variantNodes[i], structuralSettings, nestedState);
            if (!nestedResult.IsValid)
            {
                issues.AddRange(nestedResult.Issues);
            }
        }

        return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
    }
}
