// <copyright file="BindingCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates CodeableConcept and Coding elements against ValueSet bindings.
/// Uses ITerminologyService for code validation with graceful degradation.
/// Honors ValidationDepth: Minimal (skip), Spec (required only), Full (required + extensible + display).
/// </summary>
/// <remarks>
/// FHIR StructureDefinitions specify ValueSet bindings for coded elements.
/// Binding strengths:
/// - Required: Code MUST be from the ValueSet (validated by this check)
/// - Extensible: Code SHOULD be from ValueSet, but others allowed (not validated)
/// - Preferred: Code recommended but not enforced (not validated)
/// - Example: Example codes provided for guidance (not validated)
/// <para>
/// Constructed with the element's declared type, the check knows a CodeableConcept from a Coding or a code. A
/// CodeableConcept with no coding under a <c>required</c> binding is then an error at Spec and Full: a required binding
/// needs a coding from the ValueSet, and text alone is not one (R4 terminologies.html#strength). The rule needs no terminology,
/// so it holds whether or not the ValueSet is available. Compatibility depth keeps accepting text-only concepts, as the
/// Microsoft FHIR Server does. A choice element (<c>value[x]</c>) is bound through its coded variant
/// (<c>valueCode</c>, <c>valueCoding</c>, <c>valueCodeableConcept</c>).
/// </para>
/// </remarks>
public class BindingCheck : IValidationCheck
{
    private readonly string _elementPath;
    private readonly string _valueSetUrl;
    private readonly string _bindingStrength;
    private readonly ITerminologyService _terminologyService;
    private readonly string? _declaredType;

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingCheck"/> class.
    /// </summary>
    /// <param name="elementPath">The element path to validate (e.g., "gender", "status").</param>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet.</param>
    /// <param name="bindingStrength">The binding strength (Required, Extensible, Preferred, Example).</param>
    /// <param name="terminologyService">The terminology service for code validation.</param>
    public BindingCheck(
        string elementPath,
        string valueSetUrl,
        string bindingStrength,
        ITerminologyService terminologyService)
    {
        _elementPath = elementPath ?? throw new ArgumentNullException(nameof(elementPath));
        _valueSetUrl = valueSetUrl ?? throw new ArgumentNullException(nameof(valueSetUrl));
        _bindingStrength = bindingStrength ?? throw new ArgumentNullException(nameof(bindingStrength));
        _terminologyService = terminologyService ?? throw new ArgumentNullException(nameof(terminologyService));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingCheck"/> class for an element of a declared type.
    /// </summary>
    /// <param name="elementPath">The element path to validate (e.g., "gender", "status", "value[x]").</param>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet.</param>
    /// <param name="bindingStrength">The binding strength (Required, Extensible, Preferred, Example).</param>
    /// <param name="terminologyService">The terminology service for code validation.</param>
    /// <param name="declaredType">The element's declared type (e.g. <c>CodeableConcept</c>); null when it cannot be told.</param>
    public BindingCheck(
        string elementPath,
        string valueSetUrl,
        string bindingStrength,
        ITerminologyService terminologyService,
        string? declaredType)
        : this(elementPath, valueSetUrl, bindingStrength, terminologyService)
    {
        _declaredType = declaredType;
    }

    /// <summary>
    /// Validates that coded elements match their ValueSet bindings.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        // Skip terminology validation if configured
        if (settings.SkipTerminologyValidation || settings.Depth == ValidationDepth.Minimal)
        {
            return ValidationResult.Success();
        }

        var location = string.IsNullOrEmpty(element.Location)
            ? _elementPath
            : $"{element.Location}.{_elementPath}";

        // Navigate to the element. A choice element is navigated by its base name, which matches its variants.
        var pathParts = _elementPath.Split('.');
        IElement? currentElement = element;

        foreach (var part in pathParts)
        {
            var children = Bound(currentElement.Children(part.EndsWith("[x]", StringComparison.Ordinal) ? part[..^3] : part));

            // No children found - element is optional
            if (children.Count == 0)
            {
                return ValidationResult.Success();
            }

            // For multiple children (arrays), validate each
            if (children.Count > 1)
            {
                var results = new List<ValidationResult>();
                foreach (var child in children)
                {
                    var result = ValidateCodedElement(child, DeclaredTypeOf(child), LocationOf(child, location), settings).GetAwaiter().GetResult();
                    results.Add(result);
                }
                return ValidationResult.Combine(results);
            }

            // Single child - continue navigation
            currentElement = children[0];
        }

        // Reached the target element, validate it
        return ValidateCodedElement(currentElement, DeclaredTypeOf(currentElement), LocationOf(currentElement, location), settings).GetAwaiter().GetResult();
    }

    /// <summary>Where an issue on <paramref name="target"/> is reported: a choice variant at its own (concrete) location.</summary>
    private string LocationOf(IElement target, string location) =>
        _elementPath.EndsWith("[x]", StringComparison.Ordinal) && !string.IsNullOrEmpty(target.Location) ? target.Location : location;

    /// <summary>
    /// The elements the binding applies to: all of them, except that of a choice element's variants only the coded ones
    /// (code, Coding, CodeableConcept) carry the binding.
    /// </summary>
    private IReadOnlyList<IElement> Bound(IReadOnlyList<IElement> children)
    {
        if (!_elementPath.EndsWith("[x]", StringComparison.Ordinal))
        {
            return children;
        }

        return [.. children.Where(child => VariantType(child) is "code" or "Coding" or "CodeableConcept")];
    }

    /// <summary>The type of <paramref name="element"/>: the declared one, or for a choice element its variant's.</summary>
    private string? DeclaredTypeOf(IElement element) =>
        _elementPath.EndsWith("[x]", StringComparison.Ordinal) ? VariantType(element) : _declaredType;

    private string? VariantType(IElement variant)
    {
        var baseName = _elementPath.Split('.')[^1][..^3];
        if (variant.Name.Length <= baseName.Length || !variant.Name.StartsWith(baseName, StringComparison.Ordinal))
        {
            return null;
        }

        var suffix = variant.Name[baseName.Length..];
        return suffix == "Code" ? "code" : suffix;
    }

    private async Task<ValidationResult> ValidateCodedElement(
        IElement element,
        string? declaredType,
        string location,
        ValidationSettings settings)
    {
        // Check for child properties that indicate complex structures
        var hasCoding = element.Children("coding").Any();
        var hasSystem = element.Children("system").Any();
        var hasCode = element.Children("code").Any();

        if (declaredType == "CodeableConcept" && !hasCoding)
        {
            return ValidateTextOnlyConcept(element, settings);
        }

        if (hasCoding)
        {
            // This is a CodeableConcept - validate each coding
            return await ValidateCodeableConcept(element, location, settings).ConfigureAwait(false);
        }

        if (hasSystem || hasCode)
        {
            // This is a Coding - validate it
            return await ValidateCoding(element, location, settings).ConfigureAwait(false);
        }

        // Check for primitive code element (e.g., "status": "planned")
        // Primitive codes have a direct value with no child properties
        var codeValue = element.Value?.ToString();
        if (!string.IsNullOrEmpty(codeValue))
        {
            return await ValidateCode(null, codeValue, null, location, settings).ConfigureAwait(false);
        }

        // Element structure doesn't match expected patterns
        return ValidationResult.Success();
    }

    /// <summary>
    /// A CodeableConcept with no coding: under a required binding, below Compatibility depth, it cannot be from the ValueSet.
    /// Other strengths ask for nothing a text-only concept lacks.
    /// </summary>
    private ValidationResult ValidateTextOnlyConcept(IElement element, ValidationSettings settings)
    {
        if (ParseStrength(_bindingStrength) != BindingStrength.Required || settings.Depth == ValidationDepth.Compatibility)
        {
            return ValidationResult.Success();
        }

        return ValidationResult.Failure(ValidationIssue.FromTemplate(
            IssueSeverity.Error,
            "code-invalid",
            element.Location,
            IssueTemplates.BindingRequiredTextOnly,
            _valueSetUrl));
    }

    private async Task<ValidationResult> ValidateCodeableConcept(
        IElement element,
        string location,
        ValidationSettings settings)
    {
        var codings = element.Children("coding").ToList();
        if (codings.Count == 0)
        {
            // CodeableConcept without codings - check if there's a text-only representation
            // This is valid in FHIR, so return success
            return ValidationResult.Success();
        }

        // Validate each coding - at least one must be valid
        var issues = new List<ValidationIssue>();
        var hasValidCoding = false;

        foreach (var coding in codings)
        {
            var result = await ValidateCoding(coding, $"{location}.coding", settings).ConfigureAwait(false);
            issues.AddRange(result.Issues);

            // Track if we have at least one valid coding
            if (result.IsValid && !result.Issues.Any(i => i.Severity == IssueSeverity.Error))
            {
                hasValidCoding = true;
            }
        }

        // If ANY coding is valid, the CodeableConcept is valid
        // But we still want to return warnings from all validations
        if (hasValidCoding)
        {
            var warnings = issues.Where(i => i.Severity != IssueSeverity.Error).ToList();
            if (warnings.Any())
            {
                return new ValidationResult(isValid: true, issues: warnings);
            }

            return ValidationResult.Success();
        }

        // All codings failed - return combined errors
        if (issues.Count == 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, "code-invalid", location, "No valid coding found"));
        }
        return new ValidationResult(isValid: false, issues: issues);
    }

    private async Task<ValidationResult> ValidateCoding(
        IElement element,
        string location,
        ValidationSettings settings)
    {
        var systemChildren = element.Children("system");
        var codeChildren = element.Children("code");
        var displayChildren = element.Children("display");

        var system = systemChildren.Count > 0 ? systemChildren[0].Value?.ToString() : null;
        var code = codeChildren.Count > 0 ? codeChildren[0].Value?.ToString() : null;
        var display = displayChildren.Count > 0 ? displayChildren[0].Value?.ToString() : null;

        return await ValidateCode(system, code, display, location, settings).ConfigureAwait(false);
    }

    private async Task<ValidationResult> ValidateCode(
        string? system,
        string? code,
        string? display,
        string location,
        ValidationSettings settings)
    {
        var strengthEnum = ParseStrength(_bindingStrength);

        // Skip if validation depth is Spec and binding is not required
        if (settings.Depth == ValidationDepth.Spec &&
            strengthEnum is BindingStrength.Extensible or BindingStrength.Preferred or BindingStrength.Example)
        {
            return ValidationResult.Success();
        }

        try
        {
            var result = await _terminologyService.ValidateBindingAsync(
                _valueSetUrl,
                strengthEnum,
                system,
                code,
                display,
                version: null,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            // Handle different failure modes
            if (!result.IsValid || result.Severity == IssueSeverity.Error)
            {
                // An Example binding provides sample codes only: a code outside its value set is
                // never an error (the reference reports it, at most, as a warning). Downgrade those
                // misses to a non-failing warning — e.g. the Example-strength Questionnaire.item.code
                // binding, where a bare code with no system yields a warning, not an error. Extensible
                // and Preferred keep their existing (baseline) treatment here to avoid masking the
                // errors the reference still raises for codes that name a system but aren't valid in it.
                if (strengthEnum == BindingStrength.Example)
                {
                    return new ValidationResult(
                        isValid: true,
                        issues: new[]
                        {
                            ValidationIssue.FromTemplate(
                                IssueSeverity.Warning,
                                "code-invalid",
                                location,
                                IssueTemplates.BindingExampleMiss,
                                result.Message ?? "Code validation failed"),
                        });
                }

                // Terminology validation failed
                return ValidationResult.Failure(
                    ValidationIssue.FromTemplate(
                        IssueSeverity.Error,
                        "code-invalid",
                        location,
                        IssueTemplates.BindingCodeInvalid,
                        result.Message ?? "Code validation failed"));
            }

            // Check if we got a warning (unavailable terminology)
            if (result.Severity == IssueSeverity.Warning)
            {
                // Graceful degradation based on settings
                if (settings.TerminologyFailureMode == TerminologyFailureMode.Error)
                {
                    // Treat as error
                    return ValidationResult.Failure(
                        ValidationIssue.FromTemplate(
                            IssueSeverity.Error,
                            "terminology-unavailable",
                            location,
                            IssueTemplates.BindingNotChecked,
                            result.Message ?? "Terminology validation unavailable"));
                }

                // Return as warning (validation still passes)
                return new ValidationResult(
                    isValid: true,
                    issues: new[]
                    {
                        ValidationIssue.FromTemplate(
                            result.Severity, // Use the actual severity from the terminology result
                            "terminology-validation-warning", // Use a generic code as BindingValidationResult doesn't have one
                            location,
                            IssueTemplates.BindingNotChecked,
                            result.Message ?? "Terminology validation unavailable") // Use original message or fallback
                    });
            }

            // Validation succeeded or informational
            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            // Terminology service threw an exception - handle gracefully
            var severity = settings.TerminologyFailureMode == TerminologyFailureMode.Error
                ? IssueSeverity.Error
                : IssueSeverity.Warning;

            return new ValidationResult(
                isValid: severity != IssueSeverity.Error,
                issues: new[]
                {
                    ValidationIssue.FromTemplate(
                        severity,
                        "terminology-error",
                        location,
                        IssueTemplates.BindingTerminologyError,
                        ex.Message)
                });
        }
    }

    private static BindingStrength ParseStrength(string strength) =>
        strength?.ToUpperInvariant() switch
        {
            "REQUIRED" => BindingStrength.Required,
            "EXTENSIBLE" => BindingStrength.Extensible,
            "PREFERRED" => BindingStrength.Preferred,
            "EXAMPLE" => BindingStrength.Example,
            _ => BindingStrength.Example
        };
}
