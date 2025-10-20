// <copyright file="RequiredFieldCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.SourceNodeSerialization.ElementModel;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates required FHIR fields (id, resourceType, etc.).
/// Tier 1 (Fast) validator.
/// </summary>
public class RequiredFieldCheck : IValidationCheck
{
    private readonly string _fieldName;
    private readonly bool _isRequired;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredFieldCheck"/> class.
    /// </summary>
    /// <param name="fieldName">The name of the required field.</param>
    /// <param name="isRequired">Whether the field is required (default: true).</param>
    public RequiredFieldCheck(string fieldName, bool isRequired = true)
    {
        _fieldName = fieldName;
        _isRequired = isRequired;
    }

    /// <summary>
    /// Validates that a required field is present.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        if (!_isRequired)
        {
            return ValidationResult.Success();
        }

        var fieldNode = node.Children(_fieldName).FirstOrDefault();

        if (fieldNode == null)
        {
            var location = string.IsNullOrEmpty(node.Location)
                ? _fieldName
                : $"{node.Location}.{_fieldName}";

            return ValidationResult.Failure(
                ValidationIssue.InvariantFailure(
                    "required-1",
                    $"Required field '{_fieldName}' is missing",
                    location));
        }

        return ValidationResult.Success();
    }
}
