// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

/// <summary>
/// <see cref="PrimitiveElement{T}"/>'s <c>Id</c> and <c>PruneEmptyShadow</c> paths were previously
/// untested (the value/extension paths are exercised indirectly by <c>PrimitiveFidelityTests</c> and
/// <c>PrimitiveShadowTests</c> in Ignixa.Models.R4.Tests, but the shadow-object lifecycle around a bare
/// <c>id</c> was not).
/// </summary>
public sealed class PrimitiveElementTests
{
    [Fact]
    public void GivenNoShadow_WhenIdRead_ThenReturnsNull()
    {
        var element = new PrimitiveElement<string>(new JsonObject(), "birthDate");

        element.Id.ShouldBeNull();
    }

    [Fact]
    public void GivenIdSet_WhenRead_ThenShadowObjectHoldsIt()
    {
        var parent = new JsonObject();
        var element = new PrimitiveElement<string>(parent, "birthDate");

        element.Id = "a1";

        element.Id.ShouldBe("a1");
        parent["_birthDate"].ShouldNotBeNull();
        ((JsonObject)parent["_birthDate"]!)["id"]!.GetValue<string>().ShouldBe("a1");
    }

    [Fact]
    public void GivenOnlyIdSet_WhenIdSetToNull_ThenShadowObjectIsRemoved()
    {
        var parent = new JsonObject();
        var element = new PrimitiveElement<string>(parent, "birthDate");
        element.Id = "a1";

        element.Id = null;

        element.Id.ShouldBeNull();
        parent["_birthDate"].ShouldBeNull();
    }

    [Fact]
    public void GivenIdAndExtensionBothSet_WhenIdSetToNull_ThenShadowObjectSurvivesForTheExtension()
    {
        var parent = new JsonObject();
        var element = new PrimitiveElement<string>(parent, "birthDate");
        element.Id = "a1";
        element.Extension.Add(new JsonObject { ["url"] = "http://example.org/ext" });

        element.Id = null;

        element.Id.ShouldBeNull();
        parent["_birthDate"].ShouldNotBeNull();
        element.HasExtensions.ShouldBeTrue();
    }

    [Fact]
    public void GivenEmptyExtensionArrayReadButNeverPopulated_WhenPruneEmptyShadowCalled_ThenShadowIsRemoved()
    {
        var parent = new JsonObject();
        var element = new PrimitiveElement<string>(parent, "birthDate");

        // Reading Extension creates the shadow + an empty array on demand (documented caller contract).
        _ = element.Extension;
        parent["_birthDate"].ShouldNotBeNull();

        element.PruneEmptyShadow();

        parent["_birthDate"].ShouldBeNull();
        element.HasExtensions.ShouldBeFalse();
    }

    [Fact]
    public void GivenShadowWithIdAndExtensions_WhenPruneEmptyShadowCalled_ThenShadowIsKept()
    {
        var parent = new JsonObject();
        var element = new PrimitiveElement<string>(parent, "birthDate");
        element.Id = "a1";
        element.Extension.Add(new JsonObject { ["url"] = "http://example.org/ext" });

        element.PruneEmptyShadow();

        parent["_birthDate"].ShouldNotBeNull();
        element.Id.ShouldBe("a1");
        element.HasExtensions.ShouldBeTrue();
    }
}
