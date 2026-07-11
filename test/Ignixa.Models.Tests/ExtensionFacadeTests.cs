// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class ExtensionFacadeTests
{
    [Fact]
    public void GivenExtensionWithUrl_WhenReadBack_ThenValuesRoundTrip()
    {
        var ext = new Extension { Url = "http://example.org/ext1" };

        ext.Url.ShouldBe("http://example.org/ext1");
        ext.MutableNode()["url"]!.GetValue<string>().ShouldBe("http://example.org/ext1");
    }

    [Fact]
    public void GivenExtensionWithNestedExtensions_WhenAddedViaExtensions_ThenBothAreReadable()
    {
        var outer = new Extension { Url = "http://example.org/complex" };

        outer.Extensions.Add(new Ignixa.Models.R4.Extension { Url = "nested1", ValueString = "a" });
        outer.Extensions.Add(new Ignixa.Models.R4.Extension { Url = "nested2", ValueString = "b" });

        outer.Extensions.Count.ShouldBe(2);
        outer.Extensions[0].Url.ShouldBe("nested1");
        outer.Extensions[1].Url.ShouldBe("nested2");
    }

    [Fact]
    public void GivenExistingJsonObject_WhenWrappedAsExtension_ThenAllFieldsAreVisible()
    {
        var node = new JsonObject
        {
            ["url"] = "http://example.org/ext3",
        };

        var ext = new Extension(node);

        ext.Url.ShouldBe("http://example.org/ext3");
    }

    [Fact]
    public void GivenUrlAndValueUri_WhenCreatedViaRawValueUriFactory_ThenBothAreReadableAsRawJson()
    {
        var ext = Extension.CreateWithRawValueUri("http://example.org/authorize", "http://example.org/auth-endpoint");

        ext.Url.ShouldBe("http://example.org/authorize");
        ext.MutableNode()["valueUri"]!.GetValue<string>().ShouldBe("http://example.org/auth-endpoint");
    }

    [Fact]
    public void GivenExtensionCreatedViaRawValueUriFactory_WhenReadThroughTypedR4Accessor_ThenInterops()
    {
        // Proves the raw setter and the generated typed accessor both target the same underlying JSON
        // key ("valueUri") -- a value written via the low-level escape hatch is readable through the
        // normal typed R4/R5 accessor by any caller that CAN reference those packages.
        var ext = Extension.CreateWithRawValueUri("http://example.org/authorize", "http://example.org/auth-endpoint");

        var r4View = new Ignixa.Models.R4.Extension(ext.MutableNode());

        r4View.ValueUri.ShouldBe("http://example.org/auth-endpoint");
    }

    [Fact]
    public void GivenNullValueUri_WhenCreatedViaRawValueUriFactory_ThenValueUriKeyIsAbsent()
    {
        var ext = Extension.CreateWithRawValueUri("http://example.org/authorize", null);

        ext.MutableNode().ContainsKey("valueUri").ShouldBeFalse();
    }

    [Fact]
    public void GivenRawValueUriFactory_ThenNoOtherValueChoiceKeyIsEverPresent()
    {
        // CreateWithRawValueUri always constructs a brand-new Extension and sets valueUri exactly once --
        // unlike an instance mutator, there is no pre-existing state a call could conflict with, so a
        // dual-value[x]-key document (e.g. both valueUri and valueString present, which SetValueUriRaw's
        // predecessor design could not rule out) is structurally unreachable through this factory rather
        // than merely discouraged by a comment.
        var ext = Extension.CreateWithRawValueUri("http://example.org/authorize", "http://example.org/auth-endpoint");

        ext.MutableNode().Select(property => property.Key).ShouldBe(["url", "valueUri"], ignoreOrder: true);
    }

    [Fact]
    public void GivenFhirVersion_WhenCreatedViaRawValueUriFactory_ThenFhirVersionIsSet()
    {
        var ext = Extension.CreateWithRawValueUri("http://example.org/authorize", "http://example.org/auth-endpoint", FhirVersion.R4);

        ext.FhirVersion.ShouldBe(FhirVersion.R4);
    }

    [Fact]
    public void GivenExtensionWithUrl_WhenValueStringSetViaChoiceRaw_ThenReadableViaRawJson()
    {
        var ext = new Extension { Url = "http://example.org/ext1" };

        ext.SetValueChoiceRaw("valueString", "hello");

        ext.MutableNode()["valueString"]!.GetValue<string>().ShouldBe("hello");
    }

    [Fact]
    public void GivenDifferentValueChoiceAlreadySet_WhenSetValueChoiceRawCalled_ThenPriorChoiceKeyIsCleared()
    {
        // This is the exact safety property CreateWithRawValueUri's predecessor design (SetValueUriRaw,
        // a non-clearing instance mutator) could not provide: SetValueChoiceRaw derives clearing
        // structurally from FHIR's "value" + PascalCase(type) wire convention, without needing R4/R5's
        // enumerated per-version variant list, so it is safe to call more than once with a different
        // variant -- unlike the predecessor, which would have left both keys present.
        var ext = new Extension { Url = "http://example.org/ext1" };
        ext.SetValueChoiceRaw("valueString", "first");

        ext.SetValueChoiceRaw("valueUri", "http://example.org/second");

        ext.MutableNode().ContainsKey("valueString").ShouldBeFalse();
        ext.MutableNode()["valueUri"]!.GetValue<string>().ShouldBe("http://example.org/second");
    }

    [Fact]
    public void GivenValueChoiceAlreadySet_WhenSetValueChoiceRawCalledWithNull_ThenKeyIsRemoved()
    {
        var ext = new Extension { Url = "http://example.org/ext1" };
        ext.SetValueChoiceRaw("valueString", "hello");

        ext.SetValueChoiceRaw("valueString", null);

        ext.MutableNode().ContainsKey("valueString").ShouldBeFalse();
    }
}
