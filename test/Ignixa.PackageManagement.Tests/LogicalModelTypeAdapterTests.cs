// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.PackageManagement.Infrastructure;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class LogicalModelTypeAdapterTests
{
    [Theory]
    [InlineData("https://example.org/models/Model", "Model")]
    [InlineData("https://example.org/models/Model", "DifferentRoot")]
    [InlineData("urn:example:Model", "Model")]
    public void GivenLogicalCanonicalType_WhenAdapting_ThenUsesSnapshotRootNotUrlTail(string type, string path)
    {
        var result = new StructureDefinitionTypeAdapter().Adapt(Definition("logical", type, path), "5.0.0");

        result.ShouldNotBeNull();
        result.Info.Name.ShouldBe(path);
        result.Info.IsResource.ShouldBeFalse();
        result.Children.Single().Info.Name.ShouldBe("active");
    }

    [Theory]
    [InlineData("resource", "Patient", "Observation")]
    [InlineData("resource", "https://example.org/models/Model", "Model")]
    [InlineData("complex-type", "https://example.org/models/Model", "Model")]
    [InlineData("logical", "Model", "Other")]
    [InlineData("logical", "models/Model", "Model")]
    [InlineData("logical", "https://example.org/models/Model", "Model.child")]
    public void GivenIncompatibleRoot_WhenAdapting_ThenRejectsInsteadOfRelaxingCoreTypeRules(
        string kind, string type, string path)
    {
        var result = new StructureDefinitionTypeAdapter().Adapt(Definition(kind, type, path), "5.0.0");

        result.ShouldBeNull();
    }

    [Fact]
    public void GivenLogicalCanonicalWithForeignSnapshotChildren_WhenAdapting_ThenRejectsIncompleteTree()
    {
        var json = Definition("logical", "https://example.org/models/Model", "Model")
            .Replace("Model.active", "Other.active", StringComparison.Ordinal);

        var result = new StructureDefinitionTypeAdapter().Adapt(json, "5.0.0");

        result.ShouldBeNull();
    }

    [Fact]
    public void GivenResourceProfileWithDifferentNameAndCanonical_WhenAdapting_ThenRetainsBaseTypeAndResourceFlag()
    {
        var result = new StructureDefinitionTypeAdapter().Adapt(Definition("resource", "Patient", "Patient"), "4.0.1");

        result.ShouldNotBeNull();
        result.Info.Name.ShouldBe("Patient");
        result.Info.IsResource.ShouldBeTrue();
        result.Children.Single().Info.Name.ShouldBe("active");
    }

    private static string Definition(string kind, string type, string path) => $$"""
        {
          "resourceType":"StructureDefinition", "id":"profile", "name":"Profile",
          "url":"https://example.org/StructureDefinition/profile",
          "kind":"{{kind}}", "type":"{{type}}", "abstract":false,
          "snapshot":{"element":[
            {"path":"{{path}}","min":0,"max":"*"},
            {"path":"{{path}}.active","min":0,"max":"1","type":[{"code":"boolean"}]}
          ]}
        }
        """;
}
