// <copyright file="CardinalityCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates element cardinality (min..max).
/// Tier 1/2 validator - used in both Fast and Spec tiers.
/// </summary>
public class CardinalityCheck : IValidationCheck
{
    private readonly string _elementName;
    private readonly string? _shadowName;
    private readonly int _min;
    private readonly int? _max; // null = unbounded (*)

    /// <summary>
    /// Gets a value indicating whether the element name is exact - not a choice element's base name (<c>value</c>), whose
    /// children are its concrete variants - so an absent optional element can be passed from the raw JSON without a
    /// child lookup. Set by the schema builder, which knows; false for any other caller.
    /// </summary>
    internal bool ExactName
    {
        get => _shadowName is not null;
        init => _shadowName = value ? $"_{_elementName}" : null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CardinalityCheck"/> class.
    /// </summary>
    /// <param name="elementName">The name of the element to check.</param>
    /// <param name="min">Minimum cardinality.</param>
    /// <param name="max">Maximum cardinality (null = unbounded).</param>
    public CardinalityCheck(string elementName, int min, int? max)
    {
        _elementName = elementName;
        _min = min;
        _max = max;
    }

    /// <summary>
    /// Validates element cardinality.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        // 0..* constrains nothing; skipping it saves a child lookup per element on every validated node.
        if (_min == 0 && _max is null)
        {
            return ValidationResult.Success();
        }

        if (_min == 0 && _shadowName is not null && RawMembers.CertainlyAbsent(element, _elementName, _shadowName))
        {
            return ValidationResult.Success();
        }

        // Get all children with this name (IElement.Children already returns IReadOnlyList)
        var actualCount = element.Children(_elementName).Count;
        if (actualCount >= _min && (!_max.HasValue || actualCount <= _max.Value))
        {
            return ValidationResult.Success();
        }

        var location = string.IsNullOrEmpty(element.Location)
            ? _elementName
            : $"{element.Location}.{_elementName}";

        // In Compatibility mode, skip minimum cardinality checks for 2nd level+ nested elements
        // (e.g., Appointment.participant.status, Patient.contact.relationship)
        // This matches Firely SDK lenient behavior for nested backbone elements
        if (actualCount < _min && settings.Depth == ValidationDepth.Compatibility && IsNestedElement(location))
        {
            return ValidationResult.Success();
        }

        return ValidationResult.Failure(
            ValidationIssue.CardinalityViolation(location, _min, _max, actualCount));
    }

    /// <summary>
    /// Checks if an element is nested at 2nd level or deeper in the resource structure.
    /// </summary>
    /// <param name="location">The full element location path (e.g., "Appointment.participant[0].status").</param>
    /// <returns>True if the element is 2+ levels deep (has 2 or more dots separating non-empty segments).</returns>
    /// <remarks>
    /// Examples:
    /// - "Patient.name" → false (1st level, enforce cardinality)
    /// - "Appointment.participant.status" → true (2nd level, skip in compatibility mode)
    /// - "Appointment.participant[0].status" → true (2nd level with array index, skip in compatibility mode)
    /// - "Patient.contact.relationship" → true (2nd level, skip in compatibility mode)
    /// </remarks>
    private static bool IsNestedElement(string location)
    {
        // Check if there are at least 2 dots separating actual path segments
        // Handles edge cases: leading/trailing dots, empty segments
        var firstDot = location.IndexOf(".", StringComparison.Ordinal);
        if (firstDot <= 0) // -1 (no dot) or 0 (leading dot)
        {
            return false;
        }

        var secondDot = location.IndexOf(".", firstDot + 1, StringComparison.Ordinal);
        if (secondDot == -1 || secondDot == firstDot + 1) // No second dot or consecutive dots
        {
            return false;
        }

        return true;
    }
}
