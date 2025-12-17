using System.Collections.Frozen;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ignixa.NarrativeGenerator.Security;

/// <summary>
/// Sanitizes HTML to ensure it only contains FHIR-compliant XHTML elements and no XSS vectors.
/// </summary>
/// <remarks>
/// <para>
/// This sanitizer enforces the FHIR narrative XHTML specification by:
/// - Allowing only FHIR-approved HTML elements per HTML 4.0 chapters 7-11 (except section 4 of chapter 9) and 15
/// - Allowing only safe attributes (style, class, id, title, lang, href, src, alt)
/// - Removing all JavaScript vectors (javascript:, data:, vbscript:, on* handlers)
/// - Ensuring href/src attributes only use http/https schemes
/// - Adding xmlns="http://www.w3.org/1999/xhtml" to the root div element (FHIR requirement)
/// </para>
/// <para>
/// Per FHIR specification, the following are NOT allowed:
/// - HTML5 semantic elements (header, footer, section, article, aside, nav, details, summary)
/// - ARIA attributes (aria-label, aria-labelledby, aria-describedby, role)
/// - Event handlers (onclick, onload, etc.)
/// - Scripts, forms, frames, iframes, objects
/// </para>
/// </remarks>
internal partial class XhtmlSanitizer
{
    /// <summary>
    /// FHIR-allowed HTML elements per the FHIR narrative specification.
    /// Based on HTML 4.0 chapters 7-11 (except section 4 of chapter 9) and chapter 15.
    /// </summary>
    /// <remarks>
    /// HTML5 semantic elements (header, footer, section, article, aside, nav, details, summary)
    /// are explicitly NOT allowed.
    /// </remarks>
    private static readonly FrozenSet<string> AllowedElements = FrozenSet.ToFrozenSet([
        // Text elements (HTML 4.0 chapter 9)
        "div", "p", "span", "br", "hr",
        // Headers (HTML 4.0 chapter 7)
        "h1", "h2", "h3", "h4", "h5", "h6",
        // Lists (HTML 4.0 chapter 10)
        "ul", "ol", "li", "dl", "dt", "dd",
        // Tables (HTML 4.0 chapter 11)
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "colgroup", "col",
        // Formatting (HTML 4.0 chapter 9)
        "b", "i", "u", "s", "strike", "strong", "em", "small", "big", "sub", "sup",
        // Phrase elements (HTML 4.0 chapter 9)
        "code", "samp", "kbd", "var", "cite", "abbr", "acronym", "dfn",
        // Quotations (HTML 4.0 chapter 9)
        "blockquote", "q", "pre",
        // Links and images (HTML 4.0 chapter 13 and 15)
        "a", "img",
        // Address (HTML 4.0 chapter 7)
        "address"
    ]);

    /// <summary>
    /// FHIR-allowed attributes for narrative XHTML.
    /// </summary>
    /// <remarks>
    /// ARIA attributes (aria-*, role) are NOT allowed per FHIR specification.
    /// Only standard HTML 4.0 attributes are permitted.
    /// </remarks>
    private static readonly FrozenSet<string> AllowedAttributes = FrozenSet.ToFrozenSet([
        // Common attributes (per FHIR spec)
        "style", "class", "id", "title", "lang", "xml:lang", "dir", "name",
        // Link/image attributes
        "href", "src", "alt",
        // Table attributes (HTML 4.0)
        "colspan", "rowspan", "abbr", "headers", "scope",
        // Other standard attributes
        "datetime", "cite", "width", "height"
    ]);

    /// <summary>
    /// Regex pattern to detect XSS vectors in attribute values.
    /// </summary>
    /// <remarks>
    /// Detects:
    /// - javascript: URIs
    /// - data: URIs (can contain embedded scripts)
    /// - vbscript: URIs
    /// - on* event handlers (onclick, onerror, onload, etc.)
    /// </remarks>
    [GeneratedRegex(@"javascript:|data:|vbscript:|on\w+\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DisallowedPatterns();

    /// <summary>
    /// Regex to match the first div element for adding xmlns attribute.
    /// </summary>
    [GeneratedRegex(@"^(\s*<div)(\s|>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FirstDivTagRegex();

    /// <summary>
    /// Sanitizes XHTML content to remove XSS vectors while preserving FHIR-compliant markup.
    /// </summary>
    /// <param name="xhtml">The XHTML content to sanitize.</param>
    /// <returns>Sanitized XHTML safe for rendering in FHIR narratives.</returns>
    /// <exception cref="ArgumentNullException">Thrown when xhtml is null.</exception>
    /// <exception cref="System.Xml.XmlException">Thrown when xhtml is not well-formed XML.</exception>
    /// <remarks>
    /// This method also adds the required XHTML namespace (xmlns="http://www.w3.org/1999/xhtml")
    /// to the root div element as required by the FHIR narrative specification.
    /// </remarks>
    public string Sanitize(string xhtml)
    {
        ArgumentNullException.ThrowIfNull(xhtml);

        if (string.IsNullOrWhiteSpace(xhtml))
        {
            return string.Empty;
        }

        // Parse XHTML into XML document with root wrapper
        var doc = XDocument.Parse($"<root>{xhtml}</root>");

        // Sanitize all nodes in the document
        // Note: XDocument.Parse always creates a non-null Root element
        SanitizeNode(doc.Root!);

        // Get sanitized content (unwrap root)
        var result = string.Join("", doc.Root!.Nodes().Select(n => n.ToString()));

        // Add XHTML namespace to the first div element (FHIR requirement)
        // The narrative div MUST have xmlns="http://www.w3.org/1999/xhtml"
        // We use string replacement to avoid XLinq namespace propagation to child elements
        if (!result.Contains("xmlns=\"http://www.w3.org/1999/xhtml\"", StringComparison.OrdinalIgnoreCase))
        {
            result = FirstDivTagRegex().Replace(result, "$1 xmlns=\"http://www.w3.org/1999/xhtml\"$2", 1);
        }

        return result;
    }

    /// <summary>
    /// Recursively sanitizes an XML element and its descendants.
    /// </summary>
    /// <param name="element">The element to sanitize.</param>
    private void SanitizeNode(XElement element)
    {
        // Remove disallowed elements
        // Note: Using ToLowerInvariant for case-insensitive HTML element comparison (standard practice)
#pragma warning disable CA1308 // Normalize strings to uppercase
        var elementsToRemove = element.Descendants()
            .Where(e => !AllowedElements.Contains(e.Name.LocalName.ToLowerInvariant()))
            .ToList();
#pragma warning restore CA1308 // Normalize strings to uppercase

        foreach (var el in elementsToRemove)
        {
            el.Remove();
        }

        // Sanitize attributes on all remaining elements
        foreach (var el in element.Descendants())
        {
            SanitizeAttributes(el);
        }
    }

    /// <summary>
    /// Sanitizes attributes on a single element.
    /// </summary>
    /// <param name="element">The element whose attributes should be sanitized.</param>
    private void SanitizeAttributes(XElement element)
    {
        // Remove disallowed attributes or attributes with XSS vectors
        // Note: Using ToLowerInvariant for case-insensitive HTML attribute comparison (standard practice)
#pragma warning disable CA1308 // Normalize strings to uppercase
        var attrsToRemove = element.Attributes()
            .Where(a => !AllowedAttributes.Contains(a.Name.LocalName.ToLowerInvariant()) ||
                        DisallowedPatterns().IsMatch(a.Value))
            .ToList();
#pragma warning restore CA1308 // Normalize strings to uppercase

        foreach (var attr in attrsToRemove)
        {
            attr.Remove();
        }

        // Special handling for href/src - must be http/https only
        SanitizeUrlAttribute(element, "href");
        SanitizeUrlAttribute(element, "src");
    }

    /// <summary>
    /// Validates and sanitizes URL attributes (href, src) to ensure they use safe schemes.
    /// </summary>
    /// <param name="element">The element containing the attribute.</param>
    /// <param name="attributeName">The name of the URL attribute (href or src).</param>
    private static void SanitizeUrlAttribute(XElement element, string attributeName)
    {
        var attr = element.Attribute(attributeName);
        if (attr is null)
        {
            return;
        }

        var urlValue = attr.Value;

        // Remove attribute if URL is invalid or uses disallowed scheme
        if (!Uri.TryCreate(urlValue, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            attr.Remove();
        }
    }
}
