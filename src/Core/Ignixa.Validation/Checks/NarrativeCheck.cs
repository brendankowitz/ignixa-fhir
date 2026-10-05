// <copyright file="NarrativeCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIR Narrative (text) structure.
/// Ensures text.status is present and valid.
/// Ensures text.div is present when status is not 'empty'.
/// Ensures div content is well-formed XHTML without active markup.
/// Tier 1 (Fast) validator.
/// </summary>
public sealed class NarrativeCheck : IValidationCheck, ISingletonCheck
{
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    // The same formatting subset is published in STU3/R4/R4B/R5 fhir-xhtml.xsd.
    private static readonly FrozenSet<string> AllowedXhtmlElements = new[]
    {
        "a", "abbr", "acronym", "address", "area", "b", "bdo", "big", "blockquote", "br",
        "caption", "cite", "code", "col", "colgroup", "dd", "dfn", "div", "dl", "dt", "em",
        "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "img", "kbd", "li", "map", "ol",
        "p", "pre", "q", "samp", "small", "span", "strong", "sub", "sup", "table", "tbody",
        "td", "tfoot", "th", "thead", "tr", "tt", "ul", "var"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> AllowedXhtmlAttributes = new[]
    {
        "abbr", "accesskey", "align", "alt", "axis", "border", "cellpadding", "cellspacing",
        "char", "charoff", "charset", "cite", "class", "colspan", "coords", "dir", "frame",
        "headers", "height", "href", "hreflang", "id", "ismap", "lang", "longdesc", "name",
        "nohref", "rel", "rev", "rowspan", "rules", "scope", "shape", "span", "src", "style",
        "summary", "tabindex", "title", "type", "usemap", "valign", "width"
    }.ToFrozenSet(StringComparer.Ordinal);

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
    /// Parses rather than scanning text, so escaped markup, comments and CDATA remain harmless.
    /// </summary>
    private static void ValidateDivContent(IElement divNode, List<ValidationIssue> issues)
    {
        var div = divNode.Value?.ToString();
        if (string.IsNullOrEmpty(div))
        {
            issues.Add(ValidationIssue.InvariantFailure("txt-1", "Narrative div must not be empty", divNode.Location));
            return;
        }

        XDocument document;
        try
        {
            using var input = new StringReader(div);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "invalid", "Narrative div must be well-formed XHTML without DTDs or undefined entities", divNode.Location));
            return;
        }

        var root = document.Root;
        if (document.Declaration != null || root is null || root.Name != XName.Get("div", XhtmlNamespace) ||
            document.Nodes().Any(node => node != root && (node is not XText text || !string.IsNullOrWhiteSpace(text.Value))) ||
            document.DescendantNodes().OfType<XProcessingInstruction>().Any())
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "invalid", "Narrative must be a single XHTML div without an XML declaration or processing instructions", divNode.Location));
            return;
        }

        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.Name.NamespaceName != XhtmlNamespace || !AllowedXhtmlElements.Contains(node.Name.LocalName))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "invalid", $"Invalid element name in the XHTML ('{node.Name.LocalName}')",
                    divNode.Location));
            }

            foreach (var attribute in node.Attributes())
            {
                if (!IsAllowedAttribute(attribute))
                {
                    issues.Add(ValidationIssue.InvariantFailure(
                        "invalid", $"Invalid or active attribute in the XHTML ('{attribute.Name}')", divNode.Location));
                }
            }
        }

        if (string.IsNullOrWhiteSpace(root.Value) && !root.Descendants(XName.Get("img", XhtmlNamespace)).Any())
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "txt-1", "Narrative div must contain non-whitespace text or an image", divNode.Location));
        }
    }

    private static bool IsAllowedAttribute(XAttribute attribute)
    {
        if (attribute.Name == XNamespace.Xml + "space")
        {
            return attribute.Parent?.Name == XName.Get("pre", XhtmlNamespace) && attribute.Value == "preserve";
        }

        if (attribute.IsNamespaceDeclaration || attribute.Name == XNamespace.Xml + "lang")
        {
            return true;
        }

        if (attribute.Name.NamespaceName.Length != 0 || !AllowedXhtmlAttributes.Contains(attribute.Name.LocalName))
        {
            return false;
        }

        return attribute.Name.LocalName switch
        {
            "href" or "src" or "cite" or "longdesc" or "usemap" => IsPassiveUri(attribute),
            "style" => IsPassiveCss(attribute.Value),
            _ => true
        };
    }

    private static bool IsPassiveUri(XAttribute attribute)
        => IsPassiveUri(attribute.Value, attribute.Name.LocalName == "src" && attribute.Parent?.Name.LocalName == "img");

    private static bool IsPassiveUri(string uri, bool allowImageData)
    {
        // XML entities have already been decoded. Browsers also ignore embedded ASCII controls
        // in URL schemes; compare that form rather than accepting obfuscated scripting schemes.
        var value = string.Concat(uri.Where(character => !char.IsWhiteSpace(character) && !char.IsControl(character)));
        if (value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Inline raster images are supported by FHIR; HTML/SVG data must not become active content.
        return allowImageData &&
            value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPassiveCss(string css)
    {
        int position = 0;
        bool propertyExpected = true;
        while (position < css.Length)
        {
            SkipCssTrivia(css, ref position);
            if (position == css.Length)
            {
                break;
            }

            char current = css[position];
            if (current is '\'' or '"')
            {
                if (!ReadCssString(css, ref position, out _))
                {
                    return false;
                }
            }
            else if (current == ';')
            {
                propertyExpected = true;
                position++;
            }
            else if (current == '@')
            {
                position++;
                if (ReadCssIdentifier(css, ref position).Equals("import", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (IsCssNameCharacter(current) || current == '\\')
            {
                string identifier = ReadCssIdentifier(css, ref position);
                SkipCssTrivia(css, ref position);
                if (position < css.Length && css[position] == ':' && propertyExpected)
                {
                    if (identifier.Equals("behavior", StringComparison.OrdinalIgnoreCase) ||
                        identifier.Equals("-moz-binding", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    propertyExpected = false;
                    position++;
                }
                else if (position < css.Length && css[position] == '(')
                {
                    position++;
                    if (identifier.Equals("expression", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    if (identifier.Equals("url", StringComparison.OrdinalIgnoreCase) &&
                        (!ReadCssUrl(css, ref position, out string url) || !IsPassiveUri(url, allowImageData: true)))
                    {
                        return false;
                    }
                }
            }
            else
            {
                position++;
            }
        }
        return true;
    }

    private static bool IsCssNameCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or >= '\u0080';

    private static string ReadCssIdentifier(string css, ref int position)
    {
        var value = new StringBuilder();
        while (position < css.Length)
        {
            if (css[position] == '\\')
            {
                value.Append(ReadCssEscape(css, ref position));
            }
            else if (IsCssNameCharacter(css[position]))
            {
                value.Append(css[position++]);
            }
            else if (SkipCssComment(css, ref position))
            {
                // Treat comments between identifier fragments conservatively without touching strings.
            }
            else
            {
                break;
            }
        }
        return value.ToString();
    }

    private static bool ReadCssString(string css, ref int position, out string value)
    {
        char quote = css[position++];
        var text = new StringBuilder();
        while (position < css.Length && css[position] != quote)
        {
            if (css[position] == '\\')
            {
                text.Append(ReadCssEscape(css, ref position));
            }
            else
            {
                text.Append(css[position++]);
            }
        }
        value = text.ToString();
        return position < css.Length && css[position++] == quote;
    }

    private static bool ReadCssUrl(string css, ref int position, out string value)
    {
        while (position < css.Length && char.IsWhiteSpace(css[position]))
        {
            position++;
        }
        if (position < css.Length && css[position] is '\'' or '"')
        {
            if (!ReadCssString(css, ref position, out value))
            {
                return false;
            }
            SkipCssTrivia(css, ref position);
        }
        else
        {
            var text = new StringBuilder();
            while (position < css.Length && css[position] != ')')
            {
                if (css[position] == '\\')
                {
                    text.Append(ReadCssEscape(css, ref position));
                }
                else
                {
                    // An unquoted CSS URL token treats slash/star as literal URL characters.
                    text.Append(css[position++]);
                }
            }
            value = text.ToString();
        }
        return position < css.Length && css[position++] == ')';
    }

    private static string ReadCssEscape(string css, ref int position)
    {
        position++;
        int start = position;
        while (position < css.Length && position - start < 6 && char.IsAsciiHexDigit(css[position]))
        {
            position++;
        }
        if (position > start)
        {
            int codepoint = int.Parse(css.AsSpan(start, position - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (position < css.Length && char.IsWhiteSpace(css[position]))
            {
                char whitespace = css[position++];
                if (whitespace == '\r' && position < css.Length && css[position] == '\n')
                {
                    position++;
                }
            }
            return codepoint is > 0 and <= 0x10ffff and not (>= 0xd800 and <= 0xdfff)
                ? char.ConvertFromUtf32(codepoint)
                : "\ufffd";
        }
        if (position == css.Length)
        {
            return string.Empty;
        }
        char escaped = css[position++];
        return escaped is '\r' or '\n' or '\f' ? string.Empty : escaped.ToString();
    }

    private static void SkipCssTrivia(string css, ref int position)
    {
        while (position < css.Length)
        {
            if (char.IsWhiteSpace(css[position]))
            {
                position++;
            }
            else if (!SkipCssComment(css, ref position))
            {
                break;
            }
        }
    }

    private static bool SkipCssComment(string css, ref int position)
    {
        if (position + 1 >= css.Length || css[position] != '/' || css[position + 1] != '*')
        {
            return false;
        }
        int end = css.IndexOf("*/", position + 2, StringComparison.Ordinal);
        position = end < 0 ? css.Length : end + 2;
        return true;
    }
}
