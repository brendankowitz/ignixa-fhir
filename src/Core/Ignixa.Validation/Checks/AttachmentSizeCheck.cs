// <copyright file="AttachmentSizeCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that Attachment.size, when stated, matches the actual decoded byte length of
/// Attachment.data. Tier 1 (Fast) validator - a closed-world structural check with no terminology
/// or profile dependency.
/// </summary>
public sealed class AttachmentSizeCheck : IValidationCheck, ISingletonCheck
{
    /// <summary>
    /// Validates that a stated Attachment.size matches the decoded Attachment.data length.
    /// </summary>
    /// <param name="element">The Attachment element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var sizeChildren = element.Children("size");
        if (sizeChildren.Count == 0 || !TryGetIntegerValue(sizeChildren[0].Value, out var statedSize))
        {
            return ValidationResult.Success();
        }

        var dataChildren = element.Children("data");
        if (dataChildren.Count == 0)
        {
            return ValidationResult.Success();
        }

        var dataText = dataChildren[0].Value?.ToString();
        if (string.IsNullOrEmpty(dataText) || !FhirPrimitiveValidator.TryDecodeBase64(dataText, out var bytes))
        {
            // Malformed/absent base64 content is reported by TypeCheck; avoid a misleading
            // size mismatch on top of an already-invalid value.
            return ValidationResult.Success();
        }

        var actualSize = bytes!.Length;
        if (statedSize != actualSize)
        {
            return ValidationResult.Failure(ValidationIssue.InvariantFailure(
                "structure",
                $"Stated Attachment Size {statedSize} does not match actual attachment size {actualSize}",
                element.Location));
        }

        return ValidationResult.Success();
    }

    private static bool TryGetIntegerValue(object? value, out long result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
