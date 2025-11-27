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
    private readonly int _min;
    private readonly int? _max; // null = unbounded (*)

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
        // Get all children with this name (IElement.Children already returns IReadOnlyList)
        var children = element.Children(_elementName);
        var actualCount = children.Count;

        var location = string.IsNullOrEmpty(element.Location)
            ? _elementName
            : $"{element.Location}.{_elementName}";

        // Check minimum cardinality
        if (actualCount < _min)
        {
            return ValidationResult.Failure(
                ValidationIssue.CardinalityViolation(location, _min, _max, actualCount));
        }

        // Check maximum cardinality
        if (_max.HasValue && actualCount > _max.Value)
        {
            return ValidationResult.Failure(
                ValidationIssue.CardinalityViolation(location, _min, _max, actualCount));
        }

        return ValidationResult.Success();
    }
}
