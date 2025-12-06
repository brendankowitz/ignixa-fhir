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
/// </remarks>
public class BindingCheck : IValidationCheck
{
    private readonly string _elementPath;
    private readonly string _valueSetUrl;
    private readonly string _bindingStrength;
    private readonly ITerminologyService _terminologyService;

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

        // Navigate to the element
        var pathParts = _elementPath.Split('.');
        IElement? currentElement = element;

        foreach (var part in pathParts)
        {
            var children = currentElement.Children(part).ToList();

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
                    var result = ValidateCodedElement(child, location, settings).GetAwaiter().GetResult();
                    results.Add(result);
                }
                return ValidationResult.Combine(results);
            }

            // Single child - continue navigation
            currentElement = children[0];
        }

        // Reached the target element, validate it
        return ValidateCodedElement(currentElement, location, settings).GetAwaiter().GetResult();
    }

    private async Task<ValidationResult> ValidateCodedElement(
        IElement element,
        string location,
        ValidationSettings settings)
    {
        // Check for child properties that indicate complex structures
        var hasCoding = element.Children("coding").Any();
        var hasSystem = element.Children("system").Any();
        var hasCode = element.Children("code").Any();

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
            issues.Add(new ValidationIssue(IssueSeverity.Error, "code-invalid", "No valid coding found", location));
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
                // Terminology validation failed
                return ValidationResult.Failure(
                    new ValidationIssue(
                        IssueSeverity.Error,
                        "code-invalid",
                        location,
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
                        new ValidationIssue(
                            IssueSeverity.Error,
                            "terminology-unavailable",
                            location,
                            result.Message ?? "Terminology validation unavailable"));
                }

                // Return as warning (validation still passes)
                return new ValidationResult(
                    isValid: true,
                    issues: new[]
                    {
                        new ValidationIssue(
                            result.Severity, // Use the actual severity from the terminology result
                            "terminology-validation-warning", // Use a generic code as BindingValidationResult doesn't have one
                            location,
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
                    new ValidationIssue(
                        severity,
                        "terminology-error",
                        location,
                        $"Terminology validation failed: {ex.Message}")
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
