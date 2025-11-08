// <copyright file="BindingCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates CodeableConcept and Coding elements against ValueSet bindings.
/// Only validates REQUIRED bindings (not extensible, preferred, or example).
/// Uses ITerminologyService for code validation with graceful degradation.
/// Tier 3 validator - used in Profile validation tier.
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
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        // Skip terminology validation if configured
        if (settings.SkipTerminologyValidation)
        {
            return ValidationResult.Success();
        }

        // Only validate REQUIRED bindings
        if (!string.Equals(_bindingStrength, "Required", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Success();
        }

        var location = string.IsNullOrEmpty(node.Location)
            ? _elementPath
            : $"{node.Location}.{_elementPath}";

        // Navigate to the element
        var pathParts = _elementPath.Split('.');
        ISourceNode? currentNode = node;

        foreach (var part in pathParts)
        {
            var children = currentNode.Children(part).ToList();

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
            currentNode = children[0];
        }

        // Reached the target element, validate it
        return ValidateCodedElement(currentNode, location, settings).GetAwaiter().GetResult();
    }

    private async Task<ValidationResult> ValidateCodedElement(
        ISourceNode node,
        string location,
        ValidationSettings settings)
    {
        // Determine element type by checking for known child properties
        var hasCoding = node.Children("coding").Any();
        var hasSystem = node.Children("system").Any();
        var hasCode = node.Children("code").Any();

        if (hasCoding)
        {
            // This is a CodeableConcept - validate each coding
            return await ValidateCodeableConcept(node, location, settings).ConfigureAwait(false);
        }
        else if (hasSystem || hasCode)
        {
            // This is a Coding - validate it
            return await ValidateCoding(node, location, settings).ConfigureAwait(false);
        }
        else
        {
            // This might be a primitive code element
            var codeValue = node.Text;
            if (!string.IsNullOrEmpty(codeValue))
            {
                return await ValidateCode(null, codeValue, location, settings).ConfigureAwait(false);
            }
        }

        // Element structure doesn't match expected patterns
        return ValidationResult.Success();
    }

    private async Task<ValidationResult> ValidateCodeableConcept(
        ISourceNode node,
        string location,
        ValidationSettings settings)
    {
        var codings = node.Children("coding").ToList();
        if (codings.Count == 0)
        {
            // CodeableConcept without codings - check if there's a text-only representation
            // This is valid in FHIR, so return success
            return ValidationResult.Success();
        }

        // Validate each coding - at least one must be valid
        var results = new List<ValidationResult>();
        var hasValidCoding = false;

        foreach (var coding in codings)
        {
            var result = await ValidateCoding(coding, $"{location}.coding", settings).ConfigureAwait(false);
            results.Add(result);

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
            // Collect all warnings (non-error issues)
            var warnings = results.SelectMany(r => r.Issues)
                .Where(i => i.Severity == IssueSeverity.Warning)
                .ToList();

            if (warnings.Any())
            {
                return new ValidationResult(isValid: true, issues: warnings);
            }

            return ValidationResult.Success();
        }

        // All codings failed - return combined errors
        return ValidationResult.Combine(results);
    }

    private async Task<ValidationResult> ValidateCoding(
        ISourceNode node,
        string location,
        ValidationSettings settings)
    {
        var system = node.Children("system").FirstOrDefault()?.Text;
        var code = node.Children("code").FirstOrDefault()?.Text;
        var display = node.Children("display").FirstOrDefault()?.Text;

        return await ValidateCode(system, code, location, settings).ConfigureAwait(false);
    }

    private async Task<ValidationResult> ValidateCode(
        string? system,
        string? code,
        string location,
        ValidationSettings settings)
    {
        try
        {
            var result = await _terminologyService.ValidateCodeAsync(
                system,
                code,
                display: null,
                _valueSetUrl,
                CancellationToken.None).ConfigureAwait(false);

            // Handle different failure modes
            if (!result.IsValid)
            {
                // Terminology validation failed
                return ValidationResult.Failure(
                    new ValidationIssue(
                        result.Severity,
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
                            IssueSeverity.Warning,
                            "terminology-unavailable",
                            location,
                            result.Message ?? "Terminology validation unavailable")
                    });
            }

            // Validation succeeded
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
}
