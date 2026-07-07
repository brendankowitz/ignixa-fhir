// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Security-checks mode: rejects <c>string</c>-valued elements whose text contains what looks like an
/// embedded HTML tag (e.g. <c>&lt;script&gt;</c>). Off unless <see cref="ValidationSettings.SecurityChecks"/>
/// is set. Wired only for <c>string</c>-typed elements; narrative xhtml is a different type and is not
/// covered here.
/// </summary>
public sealed class EmbeddedHtmlStringCheck(string elementName) : IValidationCheck
{
    private static readonly Regex HtmlTagPattern = new(@"<[a-zA-Z/][^>]*>", RegexOptions.Compiled);

    private readonly string _elementName = elementName ?? throw new ArgumentNullException(nameof(elementName));

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (!settings.SecurityChecks)
        {
            return ValidationResult.Success();
        }

        List<ValidationIssue>? issues = null;
        foreach (var child in element.Children(_elementName))
        {
            if (!child.HasPrimitiveValue)
            {
                continue;
            }

            var text = child.Value?.ToString();
            if (!string.IsNullOrEmpty(text) && HtmlTagPattern.IsMatch(text))
            {
                (issues ??= new List<ValidationIssue>()).Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "security",
                    child.Location,
                    "The string value contains text that looks like embedded HTML tags, which are not allowed for security reasons in this context"));
            }
        }

        return issues is null ? ValidationResult.Success() : ValidationResult.Failure(issues);
    }
}
