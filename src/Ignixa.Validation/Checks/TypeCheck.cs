// <copyright file="TypeCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Globalization;
using System.Text.RegularExpressions;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIR primitive data types (string, boolean, integer, decimal, date, etc.).
/// Tier 2 (Spec) validator.
/// Uses ISourceNode.Text for primitive values.
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
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var fieldNode = node.Children(_elementName).FirstOrDefault();

        if (fieldNode == null)
        {
            return ValidationResult.Success(); // CardinalityCheck handles missing/required fields
        }

        var text = fieldNode.Text;
        var location = fieldNode.Location;

        if (string.IsNullOrEmpty(text))
        {
            // Empty text is valid for complex types (objects)
            return ValidationResult.Success();
        }

        var isValid = _expectedType switch
        {
            "string" => true, // All text is valid string
            "boolean" => text == "true" || text == "false",
            "integer" => int.TryParse(text, out _),
            "decimal" => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            "date" => DatePattern.IsMatch(text),
            "dateTime" => DateTimePattern.IsMatch(text),
            "instant" => InstantPattern.IsMatch(text), // Requires timezone (Z or +/-HH:MM)
            "time" => TimePattern.IsMatch(text),
            "id" => IdPattern.IsMatch(text),
            "uri" or "url" or "oid" or "uuid" or "code" or "markdown" or "base64Binary" => true, // Permissive for general URIs
            "canonical" => Uri.TryCreate(text, UriKind.Absolute, out _), // Canonical MUST be absolute
            _ => true // Unknown types pass
        };

        if (!isValid)
        {
            return ValidationResult.Failure(
                ValidationIssue.InvariantFailure(
                    "type-1",
                    $"Value '{text}' is not a valid '{_expectedType}'",
                    location));
        }

        return ValidationResult.Success();
    }
}
