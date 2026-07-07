// <copyright file="NarrativeCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIR Narrative (text) structure.
/// Ensures text.status is present and valid.
/// Ensures text.div is present when status is not 'empty'.
/// Ensures div content does not embed scripting/framing elements or non-predefined XML entities.
/// Tier 1 (Fast) validator.
/// </summary>
public sealed class NarrativeCheck : IValidationCheck, ISingletonCheck
{
    // Matches both opening and closing tag names ("<script", "</script") so a denied element is
    // caught regardless of which delimiter it's spotted through.
    private static readonly Regex TagNameRegex = new(@"<\s*/?\s*([A-Za-z][A-Za-z0-9]*)", RegexOptions.Compiled);

    // XML permits only these five general entities without a DTD; anything else named (e.g. the
    // HTML-only "&reg;") is not valid XHTML.
    private static readonly Regex NamedEntityRegex = new(@"&([A-Za-z][A-Za-z0-9]*);", RegexOptions.Compiled);

    private static readonly FrozenSet<string> AllowedXmlEntities =
        new[] { "amp", "lt", "gt", "apos", "quot" }.ToFrozenSet(StringComparer.Ordinal);

    // Scripting/framing/embedding elements are excluded from the narrative's basic HTML formatting
    // subset (FHIR narrative rule: HTML 4.0 chapters 7-11 except section 4 of chapter 9, and 15).
    // A deny-list (rather than a full allow-list) keeps the risk of over-rejecting legitimate
    // formatting markup low while still catching the dangerous cases this rule exists to prevent.
    private static readonly FrozenSet<string> DisallowedXhtmlElements = new[]
    {
        "script", "style", "object", "embed", "iframe", "frame", "frameset", "noframes",
        "noscript", "applet", "form", "input", "button", "select", "textarea", "link",
        "meta", "base", "title", "head", "body", "html", "svg", "video", "audio", "canvas",
        "math", "param", "source",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates Narrative structure.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var textChildren = element.Children("text");
        if (textChildren.Count == 0)
        {
            return ValidationResult.Success(); // No narrative present (optional)
        }

        var textNode = textChildren[0];
        var issues = new List<ValidationIssue>();

        // Check for status field (required if text present, except in Compatibility mode)
        var statusChildren = textNode.Children("status");
        if (statusChildren.Count == 0)
        {
            // In Compatibility mode, allow Narrative with only div (no status)
            // This matches Firely SDK behavior where status is optional
            if (settings.Depth != ValidationDepth.Compatibility)
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "txt-1",
                    "Narrative must have a status field",
                    $"{textNode.Location}.status"));
                return ValidationResult.Failure(issues);
            }

            // Compatibility mode: status is not required, but div is.
            var compatibilityDivChildren = textNode.Children("div");
            if (compatibilityDivChildren.Count == 0)
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "txt-1",
                    "Narrative must have a div field",
                    $"{textNode.Location}.div"));
                return ValidationResult.Failure(issues);
            }

            ValidateDivContent(compatibilityDivChildren[0], issues);
            return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
        }

        var statusNode = statusChildren[0];

        string? status = statusNode.Value?.ToString();
        if (status is not ("generated" or "extensions" or "additional" or "empty"))
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "txt-2",
                $"Invalid narrative status: '{status}'. Must be one of: generated, extensions, additional, empty",
                statusNode.Location));
        }

        // Check for div field (required if status is not 'empty')
        var divChildren = textNode.Children("div");
        if (status != "empty" && divChildren.Count == 0)
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "txt-1",
                "Narrative must have a div field when status is not 'empty'",
                $"{textNode.Location}.div"));
        }
        else if (divChildren.Count > 0)
        {
            ValidateDivContent(divChildren[0], issues);
        }

        if (issues.Count > 0)
        {
            return ValidationResult.Failure(issues);
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Scans narrative div content for disallowed (scripting/framing) HTML elements and for named
    /// XML entities other than the five predefined general entities.
    /// </summary>
    private static void ValidateDivContent(IElement divNode, List<ValidationIssue> issues)
    {
        var div = divNode.Value?.ToString();
        if (string.IsNullOrEmpty(div))
        {
            return;
        }

        var reportedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TagNameRegex.Matches(div))
        {
            var tagName = match.Groups[1].Value;
            if (DisallowedXhtmlElements.Contains(tagName) && reportedTags.Add(tagName))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "invalid",
                    $"Invalid element name in the XHTML ('{tagName}')",
                    divNode.Location));
            }
        }

        var reportedEntities = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in NamedEntityRegex.Matches(div))
        {
            var entityName = match.Groups[1].Value;
            if (!AllowedXmlEntities.Contains(entityName) && reportedEntities.Add(entityName))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "invalid",
                    $"Invalid entity in the XHTML ('&{entityName};')",
                    divNode.Location));
            }
        }
    }
}
