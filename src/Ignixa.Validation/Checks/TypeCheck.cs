// <copyright file="TypeCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Globalization;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIR primitive data types (string, boolean, integer, decimal, date, etc.).
/// Tier 2 (Spec) validator.
/// Uses IElement.Value for primitive values.
/// </summary>
public class TypeCheck : IValidationCheck
{
    private readonly string _elementName;
    private readonly string _expectedType;

    // Regex patterns for primitive type validation (from FHIR spec)
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9\-\.]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex DatePattern = new(@"^\d{4}(-\d{2}(-\d{2})?)?$", RegexOptions.Compiled);
    private static readonly Regex DateTimePattern = new(@"^\d{4}(-\d{2}(-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[\+\-]\d{2}:\d{2})?)?)?)?$", RegexOptions.Compiled);
    private static readonly Regex TimePattern = new(@"^\d{2}:\d{2}:\d{2}(\.\d+)?$", RegexOptions.Compiled);

    // FHIR instant: YYYY-MM-DDThh:mm:ss.sss(Z|+/-HH:MM) - timezone REQUIRED
    // Timezone is mandatory and must be Z (UTC) or explicit offset like +02:00
    private static readonly Regex InstantPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,9})?(Z|[\+\-]((0[0-9]|1[0-3]):[0-5][0-9]|14:00))$",
        RegexOptions.Compiled);

    // URI pattern: basic validation that something looks like a URI
    private static readonly Regex UriPattern = new(@"^[a-zA-Z][a-zA-Z0-9+\-.]*:", RegexOptions.Compiled);

    // URL pattern: specific validation for http/https URLs
    private static readonly Regex UrlPattern = new(@"^https?://", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeCheck"/> class.
    /// </summary>
    /// <param name="elementName">The name of the element to check.</param>
    /// <param name="expectedType">The expected FHIR type.</param>
    public TypeCheck(string elementName, string expectedType)
    {
        _elementName = elementName;
        _expectedType = expectedType;
    }

    /// <summary>
    /// Validates element type.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var fieldChildren = element.Children(_elementName);
        if (fieldChildren.Count == 0)
        {
            return ValidationResult.Success(); // CardinalityCheck handles missing/required fields
        }

        var fieldNode = fieldChildren[0];

        var text = fieldNode.Value?.ToString();
        var location = fieldNode.Location;

        if (string.IsNullOrEmpty(text))
        {
            // Empty text is valid for complex types (objects)
            return ValidationResult.Success();
        }

        // Element-name-based validation: Some FHIR types inherit from base types but have specialized rules.
        // Check element name first to apply specialized validation for inherited types.
        // Example: "id" inherits from string but requires ID format validation.
        var elementNameValidation = GetValidationByElementName(text);
        var isValid = elementNameValidation ?? GetValidationByType(text);

        if (!isValid)
        {
            // Use element name in error message if validation was element-name-based, otherwise use type
            var validationType = elementNameValidation != null ? _elementName : _expectedType;
            return ValidationResult.Failure(
                ValidationIssue.InvariantFailure(
                    "type-1",
                    $"Value '{text}' is not a valid '{validationType}'",
                    location));
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Gets validation result based on element name for specialized FHIR types.
    /// Returns null if no element-name-specific validation applies (defer to type-based validation).
    /// Only applies element-name validation if the type is a general string type, not a specific type.
    /// </summary>
    private bool? GetValidationByElementName(string text)
    {
        // Special handling for elements that inherit from base types but have specialized validation rules.
        // These checks are based on element NAME, not type, because FHIR's type system uses inheritance
        // where id/code/uri etc. extend string but have stricter rules.
        // However, if the type is more specific (like "canonical"), defer to type-based validation.

        // Only apply element-name validation if the type is string or matches the element name's implied type
        if (_expectedType != "string" && _expectedType != _elementName)
        {
            // Type is more specific than element name suggests (e.g., "url" with type "canonical")
            // Let type-based validation handle it
            return null;
        }

        return _elementName switch
        {
            // Identifier-like types
            "id" => IdPattern.IsMatch(text),

            // URI-like types
            "uri" => UriPattern.IsMatch(text),
            "url" => UrlPattern.IsMatch(text),
            "oid" => UriPattern.IsMatch(text),
            "uuid" => IsValidUuid(text),
            "canonical" => Uri.TryCreate(text, UriKind.Absolute, out _),

            // Code-like types
            "code" => true,
            "markdown" => true,

            // No element-name-specific validation (defers to type)
            _ => null
        };
    }

    /// <summary>
    /// Gets validation result based on the expected FHIR type.
    /// </summary>
    private bool GetValidationByType(string text)
    {
        return _expectedType switch
        {
            "string" => true, // All text is valid string
            // Boolean comparison is case-insensitive to handle both JSON "true"/"false"
            // and .NET boolean ToString() which returns "True"/"False"
            "boolean" => text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                        text.Equals("false", StringComparison.OrdinalIgnoreCase),
            "integer" => int.TryParse(text, out _),
            "decimal" => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            "date" => DatePattern.IsMatch(text),
            "dateTime" => DateTimePattern.IsMatch(text),
            "instant" => InstantPattern.IsMatch(text), // Requires timezone (Z or +/-HH:MM)
            "time" => TimePattern.IsMatch(text),
            "uri" or "url" or "oid" or "uuid" or "code" or "markdown" or "base64Binary" => true, // Permissive for general URIs
            "canonical" => Uri.TryCreate(text, UriKind.Absolute, out _), // Canonical MUST be absolute
            _ => true // Unknown types pass
        };
    }

    /// <summary>
    /// Validates if a string is a valid UUID in FHIR format (RFC 4122).
    /// </summary>
    private static bool IsValidUuid(string text)
    {
        return Guid.TryParse(text, out _);
    }
}
