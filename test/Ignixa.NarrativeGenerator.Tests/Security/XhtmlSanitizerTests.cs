using FluentAssertions;
using Ignixa.NarrativeGenerator.Security;

namespace Ignixa.NarrativeGenerator.Tests.Security;

/// <summary>
/// Tests for <see cref="XhtmlSanitizer"/> to ensure XSS protection in FHIR narratives.
/// </summary>
public class XhtmlSanitizerTests
{
    private readonly XhtmlSanitizer _sanitizer = new();

    #region Script Tag Removal Tests

    [Fact]
    public void GivenXhtmlWithScriptTag_WhenSanitizing_ThenRemovesScriptTag()
    {
        // Arrange
        var xhtml = "<div><p>Safe content</p><script>alert('XSS')</script></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("<script");
        result.Should().NotContain("alert");
        result.Should().Contain("<p>Safe content</p>");
    }

    [Fact]
    public void GivenXhtmlWithScriptTagInAttribute_WhenSanitizing_ThenThrowsXmlException()
    {
        // Arrange
        // Note: This is invalid XML - script tags in attribute values aren't XML-encoded
        var xhtml = "<div><p title=\"<script>alert('XSS')</script>\">Content</p></div>";

        // Act
        var act = () => _sanitizer.Sanitize(xhtml);

        // Assert
        // Invalid XML should throw - this protects against malformed input
        act.Should().Throw<System.Xml.XmlException>()
            .WithMessage("*invalid attribute character*");
    }

    [Fact]
    public void GivenXhtmlWithStyleTag_WhenSanitizing_ThenRemovesStyleTag()
    {
        // Arrange
        var xhtml = "<div><style>body { background: red; }</style><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("<style");
        result.Should().Contain("<p>Content</p>");
    }

    #endregion

    #region JavaScript URL Tests

    [Fact]
    public void GivenXhtmlWithJavascriptUrl_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"javascript:alert('XSS')\">Click me</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("javascript:");
        result.Should().Contain("<a");
        result.Should().Contain("Click me");
        result.Should().NotContain("href");
    }

    [Fact]
    public void GivenXhtmlWithJavascriptUrlMixedCase_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"JaVaScRiPt:alert('XSS')\">Click me</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("javascript:", "sanitizer should be case-insensitive");
        result.Should().NotContain("JaVaScRiPt:");
        result.Should().NotContain("href");
    }

    [Fact]
    public void GivenXhtmlWithVbscriptUrl_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"vbscript:msgbox('XSS')\">Click me</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("vbscript:");
        result.Should().NotContain("href");
    }

    #endregion

    #region Data URI Tests

    [Fact]
    public void GivenXhtmlWithDataUri_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        // Use a data URI without embedded script tags (which would be invalid XML)
        var xhtml = "<div><a href=\"data:text/html,alert('XSS')\">Click me</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("data:");
        result.Should().NotContain("href");
        result.Should().Contain("Click me");
    }

    [Fact]
    public void GivenImageWithDataUri_WhenSanitizing_ThenRemovesSrc()
    {
        // Arrange
        // Use XML-encoded data URI to avoid XML parsing errors
        var xhtml = "<div><img src=\"data:image/svg+xml,%3Csvg%3E%3C/svg%3E\" alt=\"Image\"/></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("data:");
        result.Should().NotContain("src");
        result.Should().Contain("alt=\"Image\"");
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public void GivenXhtmlWithOnClickHandler_WhenSanitizing_ThenRemovesHandler()
    {
        // Arrange
        var xhtml = "<div><button onclick=\"alert('XSS')\">Click me</button></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("onclick");
        result.Should().NotContain("alert");
    }

    [Fact]
    public void GivenXhtmlWithOnErrorHandler_WhenSanitizing_ThenRemovesHandler()
    {
        // Arrange
        var xhtml = "<div><img src=\"invalid.jpg\" onerror=\"alert('XSS')\" alt=\"Image\"/></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("onerror");
        result.Should().NotContain("alert");
    }

    [Fact]
    public void GivenXhtmlWithOnLoadHandler_WhenSanitizing_ThenRemovesHandler()
    {
        // Arrange
        var xhtml = "<div><body onload=\"alert('XSS')\">Content</body></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("onload");
        result.Should().NotContain("alert");
    }

    [Fact]
    public void GivenXhtmlWithMultipleEventHandlers_WhenSanitizing_ThenRemovesAllHandlers()
    {
        // Arrange
        var xhtml = "<div onmouseover=\"alert('XSS')\" onmouseout=\"alert('XSS2')\" onclick=\"alert('XSS3')\">Hover me</div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("onmouseover");
        result.Should().NotContain("onmouseout");
        result.Should().NotContain("onclick");
        result.Should().NotContain("alert");
        result.Should().Contain("Hover me");
    }

    #endregion

    #region FHIR Compliance Tests

    [Fact]
    public void GivenXhtmlWithoutXmlns_WhenSanitizing_ThenAddsXhtmlNamespaceToRootDiv()
    {
        // Arrange
        var xhtml = "<div class=\"fhir-narrative\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("xmlns=\"http://www.w3.org/1999/xhtml\"");
        result.Should().StartWith("<div xmlns=\"http://www.w3.org/1999/xhtml\"");
    }

    [Fact]
    public void GivenXhtmlWithExistingXmlns_WhenSanitizing_ThenDoesNotDuplicateNamespace()
    {
        // Arrange
        var xhtml = "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        // Should only have one xmlns declaration
        var namespaceCount = result.Split("xmlns=\"http://www.w3.org/1999/xhtml\"").Length - 1;
        namespaceCount.Should().Be(1);
    }

    [Fact]
    public void GivenXhtmlWithChildElements_WhenAddingXmlns_ThenChildrenDoNotInheritEmptyNamespace()
    {
        // Arrange
        var xhtml = "<div><p>Para</p><span>Span</span></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        // Child elements should NOT have xmlns="" (empty namespace)
        result.Should().NotContain("xmlns=\"\"");
    }

    [Fact]
    public void GivenXhtmlWithForbiddenHeader_WhenSanitizing_ThenRemovesHeader()
    {
        // Arrange - <header> is an HTML5 semantic element, not allowed in FHIR
        var xhtml = "<div><header>Header content</header><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("<header");
        result.Should().Contain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithForbiddenSection_WhenSanitizing_ThenRemovesSection()
    {
        // Arrange - <section> is an HTML5 semantic element, not allowed in FHIR
        var xhtml = "<div><section>Section content</section><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("<section");
        result.Should().Contain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithAriaAttributes_WhenSanitizing_ThenRemovesAriaAttributes()
    {
        // Arrange - ARIA attributes are not in FHIR's allowed attribute list
        var xhtml = "<div aria-label=\"Test\" aria-labelledby=\"heading\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("aria-label");
        result.Should().NotContain("aria-labelledby");
    }

    [Fact]
    public void GivenXhtmlWithRoleAttribute_WhenSanitizing_ThenRemovesRole()
    {
        // Arrange - role attribute is not in FHIR's allowed attribute list
        var xhtml = "<div role=\"region\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("role=");
    }

    #endregion

    #region Allowed Elements Preservation Tests

    [Fact]
    public void GivenXhtmlWithAllowedTextElements_WhenSanitizing_ThenPreservesElements()
    {
        // Arrange
        var xhtml = "<div><p>Paragraph</p><span>Span</span><br/><hr/></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        // The sanitizer adds xmlns="http://www.w3.org/1999/xhtml" to the root div per FHIR spec
        result.Should().Contain("<div");
        result.Should().Contain("xmlns=\"http://www.w3.org/1999/xhtml\"");
        result.Should().Contain("<p>Paragraph</p>");
        result.Should().Contain("<span>Span</span>");
        result.Should().Contain("<br");
        result.Should().Contain("<hr");
    }

    [Fact]
    public void GivenXhtmlWithAllowedHeaders_WhenSanitizing_ThenPreservesHeaders()
    {
        // Arrange
        var xhtml = "<div><h1>H1</h1><h2>H2</h2><h3>H3</h3><h4>H4</h4><h5>H5</h5><h6>H6</h6></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("<h1>H1</h1>");
        result.Should().Contain("<h2>H2</h2>");
        result.Should().Contain("<h3>H3</h3>");
        result.Should().Contain("<h4>H4</h4>");
        result.Should().Contain("<h5>H5</h5>");
        result.Should().Contain("<h6>H6</h6>");
    }

    [Fact]
    public void GivenXhtmlWithAllowedLists_WhenSanitizing_ThenPreservesLists()
    {
        // Arrange
        var xhtml = "<div><ul><li>Item 1</li><li>Item 2</li></ul><ol><li>First</li></ol></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("<ul>");
        result.Should().Contain("<ol>");
        result.Should().Contain("<li>Item 1</li>");
        result.Should().Contain("<li>First</li>");
    }

    [Fact]
    public void GivenXhtmlWithAllowedTable_WhenSanitizing_ThenPreservesTable()
    {
        // Arrange
        var xhtml = """
            <div>
                <table>
                    <caption>Test Table</caption>
                    <thead><tr><th>Header</th></tr></thead>
                    <tbody><tr><td>Data</td></tr></tbody>
                    <tfoot><tr><td>Footer</td></tr></tfoot>
                </table>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("<table>");
        result.Should().Contain("<caption>Test Table</caption>");
        result.Should().Contain("<thead>");
        result.Should().Contain("<tbody>");
        result.Should().Contain("<tfoot>");
        result.Should().Contain("<th>Header</th>");
        result.Should().Contain("<td>Data</td>");
    }

    [Fact]
    public void GivenXhtmlWithAllowedFormatting_WhenSanitizing_ThenPreservesFormatting()
    {
        // Arrange
        var xhtml = "<div><b>Bold</b><i>Italic</i><u>Underline</u><strong>Strong</strong><em>Emphasis</em><small>Small</small><big>Big</big><sub>Sub</sub><sup>Sup</sup></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("<b>Bold</b>");
        result.Should().Contain("<i>Italic</i>");
        result.Should().Contain("<u>Underline</u>");
        result.Should().Contain("<strong>Strong</strong>");
        result.Should().Contain("<em>Emphasis</em>");
        result.Should().Contain("<small>Small</small>");
        result.Should().Contain("<big>Big</big>");
        result.Should().Contain("<sub>Sub</sub>");
        result.Should().Contain("<sup>Sup</sup>");
    }

    #endregion

    #region Allowed Attributes Preservation Tests

    [Fact]
    public void GivenXhtmlWithSafeAttributes_WhenSanitizing_ThenPreservesAttributes()
    {
        // Arrange
        var xhtml = "<div class=\"container\" id=\"main\" title=\"Main content\" lang=\"en\" xml:lang=\"en\" dir=\"ltr\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("class=\"container\"");
        result.Should().Contain("id=\"main\"");
        result.Should().Contain("title=\"Main content\"");
        result.Should().Contain("lang=\"en\"");
        result.Should().Contain("xml:lang=\"en\"");
        result.Should().Contain("dir=\"ltr\"");
    }

    [Fact]
    public void GivenXhtmlWithSafeStyleAttribute_WhenSanitizing_ThenPreservesStyle()
    {
        // Arrange
        var xhtml = "<div style=\"color: red; font-size: 14px;\"><p>Styled content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("style=\"color: red; font-size: 14px;\"");
        result.Should().Contain("<p>Styled content</p>");
    }

    [Fact]
    public void GivenXhtmlWithHttpsUrl_WhenSanitizing_ThenPreservesUrl()
    {
        // Arrange
        var xhtml = "<div><a href=\"https://example.com\">Link</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("href=\"https://example.com\"");
        result.Should().Contain("Link");
    }

    [Fact]
    public void GivenXhtmlWithHttpUrl_WhenSanitizing_ThenPreservesUrl()
    {
        // Arrange
        var xhtml = "<div><a href=\"http://example.com\">Link</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("href=\"http://example.com\"");
    }

    [Fact]
    public void GivenImageWithHttpsSrc_WhenSanitizing_ThenPreservesSrc()
    {
        // Arrange
        var xhtml = "<div><img src=\"https://example.com/image.jpg\" alt=\"Test image\"/></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("src=\"https://example.com/image.jpg\"");
        result.Should().Contain("alt=\"Test image\"");
    }

    #endregion

    #region Disallowed Element Removal Tests

    [Fact]
    public void GivenXhtmlWithIframe_WhenSanitizing_ThenRemovesIframe()
    {
        // Arrange
        var xhtml = "<div><iframe src=\"https://evil.com\">Content</iframe><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("<iframe");
        result.Should().NotContain("evil.com");
        result.Should().Contain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithObject_WhenSanitizing_ThenRemovesObject()
    {
        // Arrange
        var xhtml = "<div><object data=\"malicious.swf\">Fallback</object><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("<object");
        result.Should().NotContain("malicious.swf");
        result.Should().Contain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithEmbed_WhenSanitizing_ThenRemovesEmbed()
    {
        // Arrange
        var xhtml = "<div><embed src=\"malicious.swf\"/><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("<embed");
        result.Should().Contain("<p>Safe</p>");
    }

    #endregion

    #region Disallowed Attribute Removal Tests

    [Fact]
    public void GivenXhtmlWithDataAttribute_WhenSanitizing_ThenRemovesDataAttribute()
    {
        // Arrange
        var xhtml = "<div data-value=\"test\" class=\"container\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("data-value");
        result.Should().Contain("class=\"container\"");
    }

    [Fact]
    public void GivenXhtmlWithRelativeUrl_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"/relative/path\">Link</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("href");
        result.Should().Contain("Link");
    }

    [Fact]
    public void GivenXhtmlWithFtpUrl_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"ftp://example.com/file.txt\">Download</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().NotContain("href");
        result.Should().NotContain("ftp:");
        result.Should().Contain("Download");
    }

    #endregion

    #region Edge Cases and Input Validation Tests

    [Fact]
    public void GivenNullXhtml_WhenSanitizing_ThenThrowsArgumentNullException()
    {
        // Arrange
        string? xhtml = null;

        // Act
        var act = () => _sanitizer.Sanitize(xhtml!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("xhtml");
    }

    [Fact]
    public void GivenEmptyXhtml_WhenSanitizing_ThenReturnsEmpty()
    {
        // Arrange
        var xhtml = string.Empty;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GivenWhitespaceXhtml_WhenSanitizing_ThenReturnsEmpty()
    {
        // Arrange
        var xhtml = "   \t\n  ";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GivenMalformedXhtml_WhenSanitizing_ThenThrowsXmlException()
    {
        // Arrange
        var xhtml = "<div><p>Unclosed paragraph</div>";

        // Act
        var act = () => _sanitizer.Sanitize(xhtml);

        // Assert
        act.Should().Throw<System.Xml.XmlException>();
    }

    [Fact]
    public void GivenComplexFhirNarrative_WhenSanitizing_ThenPreservesValidContent()
    {
        // Arrange
        var xhtml = """
            <div xmlns="http://www.w3.org/1999/xhtml">
                <h1>Patient Summary</h1>
                <p class="header">Name: <strong>John Doe</strong></p>
                <table style="border: 1px solid black;">
                    <caption>Vital Signs</caption>
                    <thead>
                        <tr>
                            <th>Measurement</th>
                            <th>Value</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td>Blood Pressure</td>
                            <td>120/80 <small>mmHg</small></td>
                        </tr>
                        <tr>
                            <td>Temperature</td>
                            <td>98.6<sup>°F</sup></td>
                        </tr>
                    </tbody>
                </table>
                <p>See <a href="https://example.com/details">full report</a> for more information.</p>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("<h1>Patient Summary</h1>");
        result.Should().Contain("<strong>John Doe</strong>");
        result.Should().Contain("<table");
        result.Should().Contain("<caption>Vital Signs</caption>");
        result.Should().Contain("<small>mmHg</small>");
        result.Should().Contain("<sup>°F</sup>");
        result.Should().Contain("href=\"https://example.com/details\"");
    }

    [Fact]
    public void GivenXhtmlWithMixedSafeAndDangerousContent_WhenSanitizing_ThenRemovesOnlyDangerousContent()
    {
        // Arrange
        // Use URL-encoded data URIs to avoid XML parsing errors
        var xhtml = """
            <div>
                <p>Safe paragraph</p>
                <script>alert('XSS')</script>
                <a href="https://safe.com">Safe link</a>
                <a href="javascript:alert('XSS')">Dangerous link</a>
                <img src="https://safe.com/image.jpg" alt="Safe"/>
                <img src="data:text/html,%3Cscript%3Ealert('XSS')%3C/script%3E" alt="Dangerous"/>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.Should().Contain("<p>Safe paragraph</p>");
        result.Should().Contain("href=\"https://safe.com\"");
        result.Should().Contain("src=\"https://safe.com/image.jpg\"");
        result.Should().NotContain("<script");
        result.Should().NotContain("javascript:");
        result.Should().NotContain("data:");
        result.Should().Contain("Dangerous link"); // Text preserved
        result.Should().Contain("alt=\"Dangerous\""); // Safe attribute preserved
    }

    #endregion
}
