// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// A CodeSystem that declares <c>supplements</c> (pointing at the base code system it augments) is a
/// supplement, and therefore must state <c>content = 'supplement'</c>. The reference validator
/// rejects any other content value: "CodeSystem Supplements SHALL have a content value of
/// 'supplement'." Closed-world — depends only on the resource's own <c>supplements</c> and
/// <c>content</c> elements.
/// </summary>
public sealed class CodeSystemSupplementContentCheck : IValidationCheck, ISingletonCheck
{
    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (element.Meta<JsonNode>() is not JsonObject root)
        {
            return ValidationResult.Success();
        }

        var supplements = (root["supplements"] as JsonValue)?.ToString();
        if (string.IsNullOrEmpty(supplements))
        {
            return ValidationResult.Success();
        }

        var content = (root["content"] as JsonValue)?.ToString();
        if (string.Equals(content, "supplement", StringComparison.Ordinal))
        {
            return ValidationResult.Success();
        }

        return ValidationResult.Failure(ValidationIssue.InvariantFailure(
            "csc-1",
            "CodeSystem Supplements SHALL have a content value of 'supplement'",
            "CodeSystem.content"));
    }
}
