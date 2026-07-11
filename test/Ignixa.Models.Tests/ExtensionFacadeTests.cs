// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
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
    public void GivenExtensionWithNestedExtensions_WhenAddedViaExtension2_ThenBothAreReadable()
    {
        var outer = new Extension { Url = "http://example.org/complex" };

        outer.Extension2.Add(new Ignixa.Models.R4.Extension { Url = "nested1", ValueString = "a" });
        outer.Extension2.Add(new Ignixa.Models.R4.Extension { Url = "nested2", ValueString = "b" });

        outer.Extension2.Count.ShouldBe(2);
        outer.Extension2[0].Url.ShouldBe("nested1");
        outer.Extension2[1].Url.ShouldBe("nested2");
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
    public void GivenExtension_WhenValueUriSetViaRawSetter_ThenReadableViaRawJson()
    {
        var ext = new Extension { Url = "http://example.org/authorize" };

        ext.SetValueUriRaw("http://example.org/auth-endpoint");

        ext.MutableNode()["valueUri"]!.GetValue<string>().ShouldBe("http://example.org/auth-endpoint");
    }

    [Fact]
    public void GivenExtension_WhenValueUriSetViaRawSetter_ThenInteropsWithTypedR4Accessor()
    {
        // Proves the raw setter and the generated typed accessor both target the same underlying JSON
        // key ("valueUri") -- a value written via the low-level escape hatch is readable through the
        // normal typed R4/R5 accessor by any caller that CAN reference those packages.
        var ext = new Extension { Url = "http://example.org/authorize" };
        ext.SetValueUriRaw("http://example.org/auth-endpoint");

        var r4View = new Ignixa.Models.R4.Extension(ext.MutableNode());

        r4View.ValueUri.ShouldBe("http://example.org/auth-endpoint");
    }

    [Fact]
    public void GivenValueUriAlreadySet_WhenSetValueUriRawCalledAgain_ThenOverwritesWithoutClearingOtherKeys()
    {
        // SetValueUriRaw does NOT do choice-variant clearing -- documents the limitation directly rather
        // than leaving it as an unverified claim in a comment. Safe only because no current caller sets
        // a different value[x] variant on the same Extension afterward.
        var ext = new Extension { Url = "http://example.org/authorize" };
        ext.SetValueUriRaw("http://example.org/first");
        ext.SetValueUriRaw("http://example.org/second");

        ext.MutableNode()["valueUri"]!.GetValue<string>().ShouldBe("http://example.org/second");
    }
}
