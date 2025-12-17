using Shouldly;
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
        result.ShouldNotContain("<script");
        result.ShouldNotContain("alert");
        result.ShouldContain("<p>Safe content</p>");
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
        Should.Throw<System.Xml.XmlException>(act).Message.ShouldContain("invalid attribute character");
    }

    [Fact]
    public void GivenXhtmlWithStyleTag_WhenSanitizing_ThenRemovesStyleTag()
    {
        // Arrange
        var xhtml = "<div><style>body { background: red; }</style><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("<style");
        result.ShouldContain("<p>Content</p>");
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
        result.ShouldNotContain("javascript:");
        result.ShouldContain("<a");
        result.ShouldContain("Click me");
        result.ShouldNotContain("href");
    }

    [Fact]
    public void GivenXhtmlWithJavascriptUrlMixedCase_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"JaVaScRiPt:alert('XSS')\">Click me</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("javascript:");
        result.ShouldNotContain("JaVaScRiPt:");
        result.ShouldNotContain("href");
    }

    [Fact]
    public void GivenXhtmlWithVbscriptUrl_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"vbscript:msgbox('XSS')\">Click me</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("vbscript:");
        result.ShouldNotContain("href");
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
        result.ShouldNotContain("data:");
        result.ShouldNotContain("href");
        result.ShouldContain("Click me");
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
        result.ShouldNotContain("data:");
        result.ShouldNotContain("src");
        result.ShouldContain("alt=\"Image\"");
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
        result.ShouldNotContain("onclick");
        result.ShouldNotContain("alert");
    }

    [Fact]
    public void GivenXhtmlWithOnErrorHandler_WhenSanitizing_ThenRemovesHandler()
    {
        // Arrange
        var xhtml = "<div><img src=\"invalid.jpg\" onerror=\"alert('XSS')\" alt=\"Image\"/></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("onerror");
        result.ShouldNotContain("alert");
    }

    [Fact]
    public void GivenXhtmlWithOnLoadHandler_WhenSanitizing_ThenRemovesHandler()
    {
        // Arrange
        var xhtml = "<div><body onload=\"alert('XSS')\">Content</body></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("onload");
        result.ShouldNotContain("alert");
    }

    [Fact]
    public void GivenXhtmlWithMultipleEventHandlers_WhenSanitizing_ThenRemovesAllHandlers()
    {
        // Arrange
        var xhtml = "<div onmouseover=\"alert('XSS')\" onmouseout=\"alert('XSS2')\" onclick=\"alert('XSS3')\">Hover me</div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("onmouseover");
        result.ShouldNotContain("onmouseout");
        result.ShouldNotContain("onclick");
        result.ShouldNotContain("alert");
        result.ShouldContain("Hover me");
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
        result.ShouldContain("xmlns=\"http://www.w3.org/1999/xhtml\"");
        result.ShouldStartWith("<div xmlns=\"http://www.w3.org/1999/xhtml\"");
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
        namespaceCount.ShouldBe(1);
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
        result.ShouldNotContain("xmlns=\"\"");
    }

    [Fact]
    public void GivenXhtmlWithForbiddenHeader_WhenSanitizing_ThenRemovesHeader()
    {
        // Arrange - <header> is an HTML5 semantic element, not allowed in FHIR
        var xhtml = "<div><header>Header content</header><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("<header");
        result.ShouldContain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithForbiddenSection_WhenSanitizing_ThenRemovesSection()
    {
        // Arrange - <section> is an HTML5 semantic element, not allowed in FHIR
        var xhtml = "<div><section>Section content</section><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("<section");
        result.ShouldContain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithAriaAttributes_WhenSanitizing_ThenRemovesAriaAttributes()
    {
        // Arrange - ARIA attributes are not in FHIR's allowed attribute list
        var xhtml = "<div aria-label=\"Test\" aria-labelledby=\"heading\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("aria-label");
        result.ShouldNotContain("aria-labelledby");
    }

    [Fact]
    public void GivenXhtmlWithRoleAttribute_WhenSanitizing_ThenRemovesRole()
    {
        // Arrange - role attribute is not in FHIR's allowed attribute list
        var xhtml = "<div role=\"region\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("role=");
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
        result.ShouldContain("<div");
        result.ShouldContain("xmlns=\"http://www.w3.org/1999/xhtml\"");
        result.ShouldContain("<p>Paragraph</p>");
        result.ShouldContain("<span>Span</span>");
        result.ShouldContain("<br");
        result.ShouldContain("<hr");
    }

    [Fact]
    public void GivenXhtmlWithAllowedHeaders_WhenSanitizing_ThenPreservesHeaders()
    {
        // Arrange
        var xhtml = "<div><h1>H1</h1><h2>H2</h2><h3>H3</h3><h4>H4</h4><h5>H5</h5><h6>H6</h6></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("<h1>H1</h1>");
        result.ShouldContain("<h2>H2</h2>");
        result.ShouldContain("<h3>H3</h3>");
        result.ShouldContain("<h4>H4</h4>");
        result.ShouldContain("<h5>H5</h5>");
        result.ShouldContain("<h6>H6</h6>");
    }

    [Fact]
    public void GivenXhtmlWithAllowedLists_WhenSanitizing_ThenPreservesLists()
    {
        // Arrange
        var xhtml = "<div><ul><li>Item 1</li><li>Item 2</li></ul><ol><li>First</li></ol></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("<ul>");
        result.ShouldContain("<ol>");
        result.ShouldContain("<li>Item 1</li>");
        result.ShouldContain("<li>First</li>");
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
        result.ShouldContain("<table>");
        result.ShouldContain("<caption>Test Table</caption>");
        result.ShouldContain("<thead>");
        result.ShouldContain("<tbody>");
        result.ShouldContain("<tfoot>");
        result.ShouldContain("<th>Header</th>");
        result.ShouldContain("<td>Data</td>");
    }

    [Fact]
    public void GivenXhtmlWithAllowedFormatting_WhenSanitizing_ThenPreservesFormatting()
    {
        // Arrange
        var xhtml = "<div><b>Bold</b><i>Italic</i><u>Underline</u><strong>Strong</strong><em>Emphasis</em><small>Small</small><big>Big</big><sub>Sub</sub><sup>Sup</sup></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("<b>Bold</b>");
        result.ShouldContain("<i>Italic</i>");
        result.ShouldContain("<u>Underline</u>");
        result.ShouldContain("<strong>Strong</strong>");
        result.ShouldContain("<em>Emphasis</em>");
        result.ShouldContain("<small>Small</small>");
        result.ShouldContain("<big>Big</big>");
        result.ShouldContain("<sub>Sub</sub>");
        result.ShouldContain("<sup>Sup</sup>");
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
        result.ShouldContain("class=\"container\"");
        result.ShouldContain("id=\"main\"");
        result.ShouldContain("title=\"Main content\"");
        result.ShouldContain("lang=\"en\"");
        result.ShouldContain("xml:lang=\"en\"");
        result.ShouldContain("dir=\"ltr\"");
    }

    [Fact]
    public void GivenXhtmlWithSafeStyleAttribute_WhenSanitizing_ThenPreservesStyle()
    {
        // Arrange
        var xhtml = "<div style=\"color: red; font-size: 14px;\"><p>Styled content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("style=\"color: red; font-size: 14px;\"");
        result.ShouldContain("<p>Styled content</p>");
    }

    [Fact]
    public void GivenXhtmlWithHttpsUrl_WhenSanitizing_ThenPreservesUrl()
    {
        // Arrange
        var xhtml = "<div><a href=\"https://example.com\">Link</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("href=\"https://example.com\"");
        result.ShouldContain("Link");
    }

    [Fact]
    public void GivenXhtmlWithHttpUrl_WhenSanitizing_ThenPreservesUrl()
    {
        // Arrange
        var xhtml = "<div><a href=\"http://example.com\">Link</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("href=\"http://example.com\"");
    }

    [Fact]
    public void GivenImageWithHttpsSrc_WhenSanitizing_ThenPreservesSrc()
    {
        // Arrange
        var xhtml = "<div><img src=\"https://example.com/image.jpg\" alt=\"Test image\"/></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("src=\"https://example.com/image.jpg\"");
        result.ShouldContain("alt=\"Test image\"");
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
        result.ShouldNotContain("<iframe");
        result.ShouldNotContain("evil.com");
        result.ShouldContain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithObject_WhenSanitizing_ThenRemovesObject()
    {
        // Arrange
        var xhtml = "<div><object data=\"malicious.swf\">Fallback</object><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("<object");
        result.ShouldNotContain("malicious.swf");
        result.ShouldContain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithEmbed_WhenSanitizing_ThenRemovesEmbed()
    {
        // Arrange
        var xhtml = "<div><embed src=\"malicious.swf\"/><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("<embed");
        result.ShouldContain("<p>Safe</p>");
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
        result.ShouldNotContain("data-value");
        result.ShouldContain("class=\"container\"");
    }

    [Fact]
    public void GivenXhtmlWithRelativeUrl_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"/relative/path\">Link</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("href");
        result.ShouldContain("Link");
    }

    [Fact]
    public void GivenXhtmlWithFtpUrl_WhenSanitizing_ThenRemovesHref()
    {
        // Arrange
        var xhtml = "<div><a href=\"ftp://example.com/file.txt\">Download</a></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("href");
        result.ShouldNotContain("ftp:");
        result.ShouldContain("Download");
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
        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("xhtml");
    }

    [Fact]
    public void GivenEmptyXhtml_WhenSanitizing_ThenReturnsEmpty()
    {
        // Arrange
        var xhtml = string.Empty;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenWhitespaceXhtml_WhenSanitizing_ThenReturnsEmpty()
    {
        // Arrange
        var xhtml = "   \t\n  ";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenMalformedXhtml_WhenSanitizing_ThenThrowsXmlException()
    {
        // Arrange
        var xhtml = "<div><p>Unclosed paragraph</div>";

        // Act
        var act = () => _sanitizer.Sanitize(xhtml);

        // Assert
        Should.Throw<System.Xml.XmlException>(act);
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
        result.ShouldContain("<h1>Patient Summary</h1>");
        result.ShouldContain("<strong>John Doe</strong>");
        result.ShouldContain("<table");
        result.ShouldContain("<caption>Vital Signs</caption>");
        result.ShouldContain("<small>mmHg</small>");
        result.ShouldContain("<sup>°F</sup>");
        result.ShouldContain("href=\"https://example.com/details\"");
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
        result.ShouldContain("<p>Safe paragraph</p>");
        result.ShouldContain("href=\"https://safe.com\"");
        result.ShouldContain("src=\"https://safe.com/image.jpg\"");
        result.ShouldNotContain("<script");
        result.ShouldNotContain("javascript:");
        result.ShouldNotContain("data:");
        result.ShouldContain("Dangerous link"); // Text preserved
        result.ShouldContain("alt=\"Dangerous\""); // Safe attribute preserved
    }

    #endregion
}
