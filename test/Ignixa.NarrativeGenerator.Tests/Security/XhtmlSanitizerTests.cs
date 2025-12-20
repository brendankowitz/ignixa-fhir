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
    public void GivenXhtmlWithForbiddenNav_WhenSanitizing_ThenRemovesNav()
    {
        // Arrange - <nav> is an HTML5 semantic element for navigation, not allowed in FHIR narratives
        var xhtml = "<div><nav>Navigation content</nav><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("<nav");
        result.ShouldContain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithForbiddenFooter_WhenSanitizing_ThenRemovesFooter()
    {
        // Arrange - <footer> is an HTML5 semantic element for footers, not allowed in FHIR narratives
        var xhtml = "<div><footer>Footer content</footer><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("<footer");
        result.ShouldContain("<p>Safe</p>");
    }

    [Fact]
    public void GivenXhtmlWithForbiddenForm_WhenSanitizing_ThenRemovesForm()
    {
        // Arrange - <form> and form elements are not allowed in FHIR narratives
        var xhtml = "<div><form><input type=\"text\"/></form><p>Safe</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("<form");
        result.ShouldNotContain("<input");
        result.ShouldContain("<p>Safe</p>");
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

    #region WCAG 2.1 AA Accessibility Tests (FHIR-53652)

    [Fact]
    public void GivenXhtmlWithAriaAttributes_WhenSanitizing_ThenPreservesAriaAttributes()
    {
        // Arrange - ARIA attributes are now allowed per FHIR-53652
        var xhtml = """
            <div role="region" aria-label="Main content" aria-labelledby="heading1" aria-describedby="desc1" aria-hidden="false">
                <h3 id="heading1">Patient Demographics</h3>
                <p id="desc1">Demographic information for the patient</p>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("role=\"region\"");
        result.ShouldContain("aria-label=\"Main content\"");
        result.ShouldContain("aria-labelledby=\"heading1\"");
        result.ShouldContain("aria-describedby=\"desc1\"");
        result.ShouldContain("aria-hidden=\"false\"");
    }

    [Fact]
    public void GivenXhtmlWithRoleAlert_WhenSanitizing_ThenPreservesRole()
    {
        // Arrange - Critical allergy alert use case
        var xhtml = """
            <div role="alert" class="fhir-alert fhir-allergy">
                <strong>SEVERE ALLERGY:</strong> Anaphylactic reaction to Penicillin
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("role=\"alert\"");
        result.ShouldContain("<strong>SEVERE ALLERGY:</strong>");
    }

    [Fact]
    public void GivenXhtmlWithRoleStatus_WhenSanitizing_ThenPreservesRole()
    {
        // Arrange - Lab results summary use case
        var xhtml = """
            <div role="status" class="fhir-summary">
                <p>Complete Blood Count: 4 abnormal values detected</p>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("role=\"status\"");
        result.ShouldContain("4 abnormal values detected");
    }

    [Fact]
    public void GivenXhtmlWithSectionElement_WhenSanitizing_ThenPreservesSection()
    {
        // Arrange - <section> is now allowed for document structure per FHIR-53652
        var xhtml = """
            <div>
                <section aria-labelledby="vital-signs-heading">
                    <h3 id="vital-signs-heading">Vital Signs</h3>
                    <p>Blood Pressure: 120/80 mmHg</p>
                </section>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("<section");
        result.ShouldContain("aria-labelledby=\"vital-signs-heading\"");
        result.ShouldContain("<h3 id=\"vital-signs-heading\">Vital Signs</h3>");
    }

    [Fact]
    public void GivenXhtmlWithFigureAndFigcaption_WhenSanitizing_ThenPreservesElements()
    {
        // Arrange - Medical imaging use case
        var xhtml = """
            <div>
                <figure>
                    <img src="https://example.com/xray.jpg" alt="Chest X-ray showing pneumonia"/>
                    <figcaption>Chest X-ray dated January 15, 2025</figcaption>
                </figure>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("<figure>");
        result.ShouldContain("<figcaption>");
        result.ShouldContain("Chest X-ray dated January 15, 2025");
    }

    [Fact]
    public void GivenXhtmlWithMarkElement_WhenSanitizing_ThenPreservesMark()
    {
        // Arrange - Highlighting abnormal lab values
        var xhtml = """
            <div>
                <p>WBC: <mark>12.5 K/µL</mark> (abnormal - reference range: 4.5-11.0)</p>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("<mark>12.5 K/µL</mark>");
    }

    [Fact]
    public void GivenXhtmlWithTimeElement_WhenSanitizing_ThenPreservesTime()
    {
        // Arrange - Semantic date/time representation
        var xhtml = """
            <div>
                <p>Lab drawn on <time datetime="2025-01-15T08:30:00Z">January 15, 2025 at 8:30 AM</time></p>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("<time");
        result.ShouldContain("datetime=\"2025-01-15T08:30:00Z\"");
        result.ShouldContain("January 15, 2025 at 8:30 AM");
    }

    [Fact]
    public void GivenXhtmlWithArticleAndAside_WhenSanitizing_ThenPreservesElements()
    {
        // Arrange - Document structure elements
        var xhtml = """
            <div>
                <article>
                    <h3>Encounter Summary</h3>
                    <p>Patient presented with fever and cough.</p>
                </article>
                <aside>
                    <p>Note: Patient has history of asthma</p>
                </aside>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("<article>");
        result.ShouldContain("<aside>");
        result.ShouldContain("Encounter Summary");
        result.ShouldContain("Note: Patient has history of asthma");
    }

    [Fact]
    public void GivenXhtmlWithDirLtr_WhenSanitizing_ThenPreservesDirAttribute()
    {
        // Arrange - Left-to-right language support
        var xhtml = "<div dir=\"ltr\"><p>English content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("dir=\"ltr\"");
    }

    [Fact]
    public void GivenXhtmlWithDirRtl_WhenSanitizing_ThenPreservesDirAttribute()
    {
        // Arrange - Right-to-left language support (Arabic, Hebrew, etc.)
        var xhtml = "<div dir=\"rtl\"><p>محتوى عربي</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("dir=\"rtl\"");
    }

    [Fact]
    public void GivenXhtmlWithDirAuto_WhenSanitizing_ThenPreservesDirAttribute()
    {
        // Arrange - Automatic direction based on content
        var xhtml = "<div dir=\"auto\"><p>Mixed content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("dir=\"auto\"");
    }

    [Fact]
    public void GivenXhtmlWithInvalidDirValue_WhenSanitizing_ThenRemovesDirAttribute()
    {
        // Arrange - Invalid dir value (only ltr, rtl, auto allowed)
        var xhtml = "<div dir=\"invalid\" class=\"content\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldNotContain("dir=\"invalid\"");
        result.ShouldContain("class=\"content\""); // Other attributes preserved
    }

    [Fact]
    public void GivenXhtmlWithDirUpperCase_WhenSanitizing_ThenPreservesDirAttribute()
    {
        // Arrange - Dir attribute is case-insensitive
        var xhtml = "<div dir=\"LTR\"><p>Content</p></div>";

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("dir=\"LTR\"");
    }

    [Fact]
    public void GivenXhtmlWithAccessibleTable_WhenSanitizing_ThenPreservesAriaAttributes()
    {
        // Arrange - Accessible table with ARIA
        var xhtml = """
            <div>
                <table role="table" aria-label="Laboratory Results - Complete Blood Count">
                    <caption>Lab Results</caption>
                    <thead>
                        <tr role="row">
                            <th role="columnheader" scope="col">Test</th>
                            <th role="columnheader" scope="col">Result</th>
                            <th role="columnheader" scope="col">Reference Range</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr role="row">
                            <td role="cell">WBC</td>
                            <td role="cell"><mark>12.5 K/µL</mark></td>
                            <td role="cell">4.5-11.0 K/µL</td>
                        </tr>
                    </tbody>
                </table>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert
        result.ShouldContain("role=\"table\"");
        result.ShouldContain("aria-label=\"Laboratory Results - Complete Blood Count\"");
        result.ShouldContain("role=\"row\"");
        result.ShouldContain("role=\"columnheader\"");
        result.ShouldContain("role=\"cell\"");
        result.ShouldContain("<mark>12.5 K/µL</mark>");
    }

    [Fact]
    public void GivenCompleteAccessibilityExample_WhenSanitizing_ThenPreservesAllAccessibilityFeatures()
    {
        // Arrange - Comprehensive WCAG 2.1 AA compliant narrative
        var xhtml = """
            <div xmlns="http://www.w3.org/1999/xhtml" lang="en" dir="ltr">
                <div role="alert" class="fhir-alert">
                    <strong>CRITICAL ALERT:</strong> Severe penicillin allergy
                </div>

                <section aria-labelledby="demographics-heading">
                    <h3 id="demographics-heading">Patient Demographics</h3>
                    <p>Date of Birth: <time datetime="1985-06-15">June 15, 1985</time></p>
                </section>

                <section aria-labelledby="labs-heading">
                    <h3 id="labs-heading">Laboratory Results</h3>
                    <figure>
                        <table role="table" aria-label="Complete Blood Count">
                            <caption>CBC Results</caption>
                            <tr role="row">
                                <th role="columnheader">Test</th>
                                <th role="columnheader">Value</th>
                            </tr>
                            <tr role="row">
                                <td role="cell">WBC</td>
                                <td role="cell"><mark aria-label="Abnormal result">12.5 K/µL</mark></td>
                            </tr>
                        </table>
                        <figcaption>Lab results from <time datetime="2025-01-15">January 15, 2025</time></figcaption>
                    </figure>
                </section>

                <aside aria-label="Clinical note">
                    <p>Patient requires close monitoring</p>
                </aside>
            </div>
            """;

        // Act
        var result = _sanitizer.Sanitize(xhtml);

        // Assert - Verify all accessibility features are preserved
        result.ShouldContain("role=\"alert\"");
        result.ShouldContain("role=\"table\"");
        result.ShouldContain("role=\"row\"");
        result.ShouldContain("role=\"columnheader\"");
        result.ShouldContain("role=\"cell\"");
        result.ShouldContain("aria-labelledby=\"demographics-heading\"");
        result.ShouldContain("aria-labelledby=\"labs-heading\"");
        result.ShouldContain("aria-label=\"Complete Blood Count\"");
        result.ShouldContain("aria-label=\"Abnormal result\"");
        result.ShouldContain("aria-label=\"Clinical note\"");
        result.ShouldContain("<section");
        result.ShouldContain("<figure>");
        result.ShouldContain("<figcaption>");
        result.ShouldContain("<mark");
        result.ShouldContain("<time");
        result.ShouldContain("<aside");
        result.ShouldContain("dir=\"ltr\"");
        result.ShouldContain("lang=\"en\"");
    }

    #endregion
}
