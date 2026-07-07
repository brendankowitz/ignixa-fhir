// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// noHtmlInMarkdown mode: flags <c>markdown</c>-valued elements whose text contains what appears to be
/// an embedded, un-escaped HTML tag. Off unless <see cref="ValidationSettings.NoHtmlInMarkdown"/> is set.
/// See HL7 JIRA FHIR-38714.
/// </summary>
public sealed class MarkdownHtmlCheck(string elementName) : IValidationCheck
{
    // An angle bracket immediately followed by a letter is the start of an HTML-like tag in markdown.
    private static readonly Regex EmbeddedTagPattern = new(@"<[a-zA-Z]", RegexOptions.Compiled);

    private readonly string _elementName = elementName ?? throw new ArgumentNullException(nameof(elementName));

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (!settings.NoHtmlInMarkdown)
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
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var match = EmbeddedTagPattern.Match(text);
            if (match.Success)
            {
                (issues ??= new List<ValidationIssue>()).Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "invalid",
                    child.Location,
                    $"The markdown contains content that appears to be an embedded HTML tag starting at '{match.Value}'. This will (or SHOULD) be escaped by the presentation layer. The content should be checked to confirm that this is the desired behaviour"));
            }
        }

        return issues is null ? ValidationResult.Success() : ValidationResult.Failure(issues);
    }
}
