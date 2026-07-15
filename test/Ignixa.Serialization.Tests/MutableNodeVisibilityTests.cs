// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using System.Text.Json.Nodes;
using Ignixa.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class MutableNodeVisibilityTests
{
    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "example"
        }
        """;

    [Fact]
    public void GivenResourceJsonNode_WhenInspectingPublicApi_ThenMutableNodeIsNotPublic()
    {
        AssertMutableNodeIsNotPublicOrProtected(typeof(BaseJsonNode));

        typeof(ResourceJsonNode)
            .GetProperty("MutableNode", BindingFlags.Instance | BindingFlags.Public)
            .ShouldBeNull();

        typeof(Parameters)
            .GetProperty("MutableNode", BindingFlags.Instance | BindingFlags.Public)
            .ShouldBeNull();
    }

    private static void AssertMutableNodeIsNotPublicOrProtected(Type type)
    {
        type.GetProperty("MutableNode", BindingFlags.Instance | BindingFlags.Public)
            .ShouldBeNull();

        var property = type.GetProperty("MutableNode", BindingFlags.Instance | BindingFlags.NonPublic);
        property.ShouldNotBeNull();
        var getter = property!.GetMethod;
        getter.ShouldNotBeNull();
        getter!.IsAssembly.ShouldBeTrue();
        getter.IsFamily.ShouldBeFalse();
        getter.IsFamilyOrAssembly.ShouldBeFalse();
    }

    [Fact]
    public void GivenResourceJsonNode_WhenUsingExplicitMutableInterface_ThenRawJsonObjectIsAvailable()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);

        var jsonObject = ((IMutableJsonNode)resource).MutableNode;

        jsonObject["resourceType"]!.GetValue<string>().ShouldBe("Patient");
        jsonObject["id"]!.GetValue<string>().ShouldBe("example");
    }

    [Fact]
    public void GivenResourceJsonNode_WhenUsingNavigatorMetaJsonNode_ThenRawJsonNodeIsAvailable()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        var explicitNode = ((IMutableJsonNode)resource).MutableNode;

        var navigatorNode = resource.ToSourceNavigator().Meta<JsonNode>();

        navigatorNode.ShouldBeSameAs(explicitNode);
    }
}
