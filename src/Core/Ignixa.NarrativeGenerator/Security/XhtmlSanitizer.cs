using System.Collections.Frozen;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ignixa.NarrativeGenerator.Security;

/// <summary>
/// Sanitizes HTML to ensure it only contains FHIR-compliant XHTML elements and no XSS vectors.
/// </summary>
/// <remarks>
/// This sanitizer enforces the FHIR narrative XHTML specification by:
/// - Allowing only FHIR-approved HTML elements (div, p, span, tables, lists, etc.)
/// - Allowing only safe attributes (style, class, id, title, lang, href, src, alt)
/// - Removing all JavaScript vectors (javascript:, data:, vbscript:, on* handlers)
/// - Ensuring href/src attributes only use http/https schemes
/// </remarks>
public partial class XhtmlSanitizer
{
    /// <summary>
    /// FHIR-allowed HTML elements per the FHIR narrative specification.
    /// </summary>
    private static readonly FrozenSet<string> AllowedElements = FrozenSet.ToFrozenSet([
        // Text elements
        "div", "p", "span", "br", "hr",
        // Headers
        "h1", "h2", "h3", "h4", "h5", "h6",
        // Lists
        "ul", "ol", "li", "dl", "dt", "dd",
        // Tables
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption",
        // Formatting
        "b", "i", "u", "strong", "em", "small", "big", "sub", "sup",
        // Links and images
        "a", "img"
    ]);

    /// <summary>
    /// FHIR-allowed attributes for narrative XHTML.
    /// </summary>
    private static readonly FrozenSet<string> AllowedAttributes = FrozenSet.ToFrozenSet([
        // Common attributes
        "style", "class", "id", "title", "lang", "xml:lang", "dir",
        // Link/image attributes
        "href", "src", "alt"
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
    /// Sanitizes XHTML content to remove XSS vectors while preserving FHIR-compliant markup.
    /// </summary>
    /// <param name="xhtml">The XHTML content to sanitize.</param>
    /// <returns>Sanitized XHTML safe for rendering in FHIR narratives.</returns>
    /// <exception cref="ArgumentNullException">Thrown when xhtml is null.</exception>
    /// <exception cref="System.Xml.XmlException">Thrown when xhtml is not well-formed XML.</exception>
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

        // Return inner content only (unwrap root)
        return string.Join("", doc.Root!.Nodes().Select(n => n.ToString()));
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
