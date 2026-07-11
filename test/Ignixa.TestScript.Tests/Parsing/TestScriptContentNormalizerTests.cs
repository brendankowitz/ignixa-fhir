using System.Text.Json.Nodes;
using Ignixa.TestScript.Parsing;

namespace Ignixa.TestScript.Tests.Parsing;

public class TestScriptContentNormalizerTests
{
    [Fact]
    public void GivenRootWithoutShorthandOrExtension_WhenNormalized_ThenObjectUnchanged()
    {
        var root = JsonNode.Parse("""{"resourceType":"TestScript","name":"Plain","status":"active"}""")!.AsObject();

        var normalized = TestScriptContentNormalizer.Normalize(root);

        normalized["requiresCapability"].ShouldBeNull();
        normalized["extension"].ShouldBeNull();
    }

    [Fact]
    public void GivenRootShorthandOnly_WhenNormalized_ThenRewrittenToCanonicalExtension()
    {
        var root = JsonNode.Parse("""
            {"resourceType":"TestScript","name":"SuiteGated","status":"active","requiresCapability":"rest.exists()"}
            """)!.AsObject();

        var normalized = TestScriptContentNormalizer.Normalize(root);

        normalized["requiresCapability"].ShouldBeNull();
        var extensions = normalized["extension"]!.AsArray();
        extensions.Count.ShouldBe(1);
        extensions[0]!["url"]!.GetValue<string>().ShouldBe(TestScriptContentNormalizer.RequiresCapabilityUrl);
        extensions[0]!["valueString"]!.GetValue<string>().ShouldBe("rest.exists()");
    }

    [Fact]
    public void GivenRootShorthandAlongsideUnrelatedExtension_WhenNormalized_ThenBothExtensionsPresent()
    {
        var root = JsonNode.Parse("""
            {
              "resourceType":"TestScript","name":"Combined","status":"active",
              "requiresCapability":"rest.exists()",
              "extension":[{"url":"http://ignixa.io/testscript/fhirVersions","valueString":"4.0"}]
            }
            """)!.AsObject();

        var normalized = TestScriptContentNormalizer.Normalize(root);

        var extensions = normalized["extension"]!.AsArray();
        extensions.Count.ShouldBe(2);
        extensions.Any(e => e!["url"]!.GetValue<string>() == "http://ignixa.io/testscript/fhirVersions").ShouldBeTrue();
        extensions.Any(e => e!["url"]!.GetValue<string>() == TestScriptContentNormalizer.RequiresCapabilityUrl).ShouldBeTrue();
    }

    [Fact]
    public void GivenIdenticalShorthandAndCanonicalExtension_WhenNormalized_ThenShorthandRemovedNoDuplicate()
    {
        var root = JsonNode.Parse("""
            {
              "resourceType":"TestScript","name":"Identical","status":"active",
              "requiresCapability":"rest.exists()",
              "extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"rest.exists()"}]
            }
            """)!.AsObject();

        var normalized = TestScriptContentNormalizer.Normalize(root);

        normalized["requiresCapability"].ShouldBeNull();
        var extensions = normalized["extension"]!.AsArray();
        extensions.Count.ShouldBe(1);
        extensions[0]!["valueString"]!.GetValue<string>().ShouldBe("rest.exists()");
    }

    [Fact]
    public void GivenConflictingShorthandAndCanonicalExtension_WhenNormalized_ThenThrows()
    {
        var root = JsonNode.Parse("""
            {
              "resourceType":"TestScript","name":"Conflicting","status":"active",
              "requiresCapability":"rest.exists()",
              "extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"other.exists()"}]
            }
            """)!.AsObject();

        var ex = Should.Throw<TestScriptNormalizationException>(() => TestScriptContentNormalizer.Normalize(root));
        ex.Message.ShouldContain("conflicting");
    }

    [Fact]
    public void GivenNonStringShorthandValue_WhenNormalized_ThenThrows()
    {
        var root = JsonNode.Parse("""
            {"resourceType":"TestScript","name":"Malformed","status":"active","requiresCapability":true}
            """)!.AsObject();

        var ex = Should.Throw<TestScriptNormalizationException>(() => TestScriptContentNormalizer.Normalize(root));
        ex.Message.ShouldContain("requiresCapability");
    }

    [Fact]
    public void GivenTestEntryShorthand_WhenNormalized_ThenRewrittenOnThatTestOnly()
    {
        var root = JsonNode.Parse("""
            {
              "resourceType":"TestScript","name":"TestLevel","status":"active",
              "test":[
                {"name":"gated","requiresCapability":"rest.resource.exists()","action":[]},
                {"name":"ungated","action":[]}
              ]
            }
            """)!.AsObject();

        var normalized = TestScriptContentNormalizer.Normalize(root);

        var tests = normalized["test"]!.AsArray();
        tests[0]!["requiresCapability"].ShouldBeNull();
        var gatedExtensions = tests[0]!["extension"]!.AsArray();
        gatedExtensions.Count.ShouldBe(1);
        gatedExtensions[0]!["valueString"]!.GetValue<string>().ShouldBe("rest.resource.exists()");
        tests[1]!.AsObject().ContainsKey("extension").ShouldBeFalse();
    }

    [Fact]
    public void GivenConflictingShorthandOnOneTestAmongMany_WhenNormalized_ThenThrowsWithTestPath()
    {
        var root = JsonNode.Parse("""
            {
              "resourceType":"TestScript","name":"MultiTest","status":"active",
              "test":[
                {"name":"ok","action":[]},
                {
                  "name":"conflict",
                  "requiresCapability":"a.exists()",
                  "extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"b.exists()"}],
                  "action":[]
                }
              ]
            }
            """)!.AsObject();

        var ex = Should.Throw<TestScriptNormalizationException>(() => TestScriptContentNormalizer.Normalize(root));
        ex.Message.ShouldContain("test[1]");
    }

    [Fact]
    public void GivenUnrelatedUnknownProperty_WhenNormalized_ThenLeftUntouched()
    {
        var root = JsonNode.Parse("""
            {"resourceType":"TestScript","name":"Unknown","status":"active","someUnknownField":"whatever"}
            """)!.AsObject();

        var normalized = TestScriptContentNormalizer.Normalize(root);

        normalized["someUnknownField"]!.GetValue<string>().ShouldBe("whatever");
    }

    [Fact]
    public void GivenShorthandWithNonStringExistingExtensionValueType_WhenNormalized_ThenThrowsWithoutDuplicateEntry()
    {
        // The existing extension entry recognizably matches the canonical requiresCapability url but
        // uses valueBoolean instead of valueString. This must be treated as a malformed canonical form
        // rather than silently discarding the shorthand or producing a second same-url extension entry.
        var root = JsonNode.Parse("""
            {
              "resourceType":"TestScript","name":"WrongCanonicalType","status":"active",
              "requiresCapability":"rest.exists()",
              "extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueBoolean":true}]
            }
            """)!.AsObject();

        var ex = Should.Throw<TestScriptNormalizationException>(() => TestScriptContentNormalizer.Normalize(root));
        ex.Message.ShouldContain(TestScriptContentNormalizer.RequiresCapabilityUrl);
    }

    [Fact]
    public void GivenShorthandWithCanonicalExtensionMissingValueString_WhenNormalized_ThenThrows()
    {
        var root = JsonNode.Parse("""
            {
              "resourceType":"TestScript","name":"MissingValueString","status":"active",
              "requiresCapability":"rest.exists()",
              "extension":[{"url":"http://ignixa.io/testscript/requiresCapability"}]
            }
            """)!.AsObject();

        Should.Throw<TestScriptNormalizationException>(() => TestScriptContentNormalizer.Normalize(root));
    }

    [Fact]
    public void GivenUnrelatedExtensionWithNonStringUrl_WhenNormalized_ThenNotThrownAndTreatedAsNonMatch()
    {
        // An unrelated extension entry with a non-string 'url' (e.g. malformed authoring input) must not
        // crash normalization; it should simply be treated as not matching the canonical extension.
        var root = JsonNode.Parse("""
            {
              "resourceType":"TestScript","name":"NonStringUrl","status":"active",
              "requiresCapability":"rest.exists()",
              "extension":[{"url":123,"valueString":"irrelevant"}]
            }
            """)!.AsObject();

        var normalized = TestScriptContentNormalizer.Normalize(root);

        normalized["requiresCapability"].ShouldBeNull();
        var extensions = normalized["extension"]!.AsArray();
        extensions.Count.ShouldBe(2);
        extensions.Any(e => e!["url"] is JsonValue urlValue && urlValue.TryGetValue<string>(out var url) &&
                             url == TestScriptContentNormalizer.RequiresCapabilityUrl &&
                             e["valueString"]!.GetValue<string>() == "rest.exists()").ShouldBeTrue();
    }

    [Fact]
    public void GivenRootObject_WhenNormalized_ThenOriginalInputIsNotMutated()
    {
        var root = JsonNode.Parse("""
            {"resourceType":"TestScript","name":"Immutable","status":"active","requiresCapability":"rest.exists()"}
            """)!.AsObject();

        TestScriptContentNormalizer.Normalize(root);

        root["requiresCapability"]!.GetValue<string>().ShouldBe("rest.exists()");
        root["extension"].ShouldBeNull();
    }

    [Fact]
    public void GivenNullRoot_WhenNormalized_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => TestScriptContentNormalizer.Normalize(null!));
    }
}
