using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

public class NarrativePipelineSafetyTests
{
    [Theory]
    [InlineData("<p>unclosed")]
    [InlineData("<p onclick='alert(1)'>text</p>")]
    [InlineData("<a href='jav&#x09;ascript:alert(1)'>link</a>")]
    [InlineData("<p style='width:expre/**/ssion(alert(1))'>text</p>")]
    [InlineData("<p style='width:e\\78pression(alert(1))'>text</p>")]
    [InlineData("<p style='background:url(javascript:alert(1))'>text</p>")]
    [InlineData("<p style='background-image:URL(\"jav&#x09;ascript:alert(1)\")'>text</p>")]
    [InlineData("<p style='behavior:url(https://example.org/active.htc)'>text</p>")]
    [InlineData("<p style='-moz-binding:url(https://example.org/active.xml)'>text</p>")]
    [InlineData("<p style='@import \"https://example.org/active.css\"'>text</p>")]
    [InlineData("<p xml:space='preserve'>text</p>")]
    [InlineData("<pre xml:space='default'>text</pre>")]
    [InlineData("<img%20src='example'/>")]
    [InlineData("<img src='data:text/html;base64,PHNjcmlwdD4='/>")]
    [InlineData("<img src='data:image/svg+xml;base64,PHN2Zz4='/>")]
    [InlineData("<unknown>text</unknown>")]
    [InlineData("<p xmlns='urn:other'>text</p>")]
    [InlineData("<a xmlns:xlink='http://www.w3.org/1999/xlink' xlink:href='https://example.org'>text</a>")]
    [InlineData("   ")]
    public void GivenMalformedOrActiveMarkup_WhenCheckingNarrative_ThenRejects(string fragment)
    {
        var result = Validate($"<div xmlns='http://www.w3.org/1999/xhtml'>{fragment}</div>");
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Path.Contains("text", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<![CDATA[<script>not markup</script>]]>")]
    [InlineData("<!-- <script>not markup</script> --> text")]
    [InlineData("&lt;script&gt; &#160; &amp; text")]
    [InlineData("%3cscript src=http://www.example.com/malicious-code.js%3e%3c/script%3e")]
    [InlineData("\\x3cscript src=http://www.example.com/malicious-code.js\\x3e\\x3c/script\\x3e")]
    [InlineData("' SELECT name FROM syscolumns WHERE id = 1 --")]
    [InlineData("<p title='javascript: is ordinary text' style='color:red; font-weight:bold'>text</p>")]
    [InlineData("<pre xml:space='preserve'>A  B\n  C</pre>")]
    [InlineData("<p style=\"background-image:url('https://example.org/images/javascript:logo.png')\">text</p>")]
    [InlineData("<p style=\"font-family:'@import'; color:red\">text</p>")]
    [InlineData("<p style=\"font-family:'javascript:logo'; color:red\">text</p>")]
    [InlineData("<p style=\"font-family:'expression('; color:red\">text</p>")]
    [InlineData("<p style=\"font-family:'a\\'@import'; color:red\">text</p>")]
    [InlineData("<p style=\"background-image:url('https://example.org/@import/behavior:logo.png')\">text</p>")]
    [InlineData("<p style=\"background-image:url(https://example.org/images/*/logo.png)\">text</p>")]
    [InlineData("<p style=\"background-image:url(https://example.org/images/*logo.png)\">text</p>")]
    [InlineData("<p style=\"background-image:url(/*/logo.png)\">text</p>")]
    [InlineData("<img src='#image' alt='image'/>")]
    [InlineData("<img src='data:image/avif;base64,aGVsbG8=' alt='image'/>")]
    [InlineData("<a href='mailto:example@example.org'>contact</a>")]
    public void GivenHarmlessNarrative_WhenChecking_ThenDoesNotTreatTextAsMarkup(string fragment)
    {
        Validate($"<div xmlns='http://www.w3.org/1999/xhtml'>{fragment}</div>").IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("<div>missing namespace</div>")]
    [InlineData("<p xmlns='http://www.w3.org/1999/xhtml'>wrong root</p>")]
    [InlineData("<?xml version='1.0'?><div xmlns='http://www.w3.org/1999/xhtml'>text</div>")]
    [InlineData("<!DOCTYPE div [<!ENTITY text 'payload'>]><div xmlns='http://www.w3.org/1999/xhtml'>&text;</div>")]
    [InlineData("<div xmlns='http://www.w3.org/1999/xhtml'><?processing data?>text</div>")]
    public void GivenInvalidFragmentEnvelope_WhenChecking_ThenRejectsWithoutResolvingXmlEntities(string narrative)
    {
        Validate(narrative).IsValid.ShouldBeFalse();
    }

    private static ValidationResult Validate(string narrative)
    {
        var resource = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["text"] = new JsonObject { ["status"] = "generated", ["div"] = narrative }
        };
        var element = JsonSourceNodeFactory.Parse(resource.ToJsonString()).ToElement(TestSchemaProvider.GetR4Schema());
        return new NarrativeCheck().Validate(element, new ValidationSettings { Depth = ValidationDepth.Minimal }, ValidationState.ForRoot(element));
    }
}
