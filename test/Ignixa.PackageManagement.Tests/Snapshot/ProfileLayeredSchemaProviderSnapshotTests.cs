// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests.Snapshot;

/// <summary>
/// Verifies the wiring seam: <see cref="ProfileLayeredSchemaProvider"/> backfills a snapshot for a
/// differential-only profile before adapting it, so profiles that ship without a snapshot are no
/// longer silently dropped. Uses a package-supplied base (profile-on-profile) so no core provider
/// is required.
/// </summary>
public sealed class ProfileLayeredSchemaProviderSnapshotTests
{
    private const string BaseSd = """
    {
      "resourceType":"StructureDefinition","id":"WidgetBase","type":"Widget",
      "url":"http://example.org/StructureDefinition/WidgetBase","kind":"resource",
      "snapshot":{"element":[
        {"path":"Widget","min":0,"max":"*"},
        {"path":"Widget.label","min":0,"max":"1","type":[{"code":"string"}]}
      ]}
    }
    """;

    private const string DifferentialOnlyProfile = """
    {
      "resourceType":"StructureDefinition","id":"WidgetProfile","type":"Widget",
      "url":"http://example.org/StructureDefinition/WidgetProfile","kind":"resource",
      "baseDefinition":"http://example.org/StructureDefinition/WidgetBase",
      "differential":{"element":[{"path":"Widget.label","min":1}]}
    }
    """;

    [Fact]
    public void GivenDifferentialOnlyProfileWithPackageBase_WhenLayered_ThenProfileIsAdaptedWithTightenedCardinality()
    {
        var baseProvider = Substitute.For<IFhirSchemaProvider>();
        baseProvider.FullVersion.Returns("4.0.1");
        baseProvider.IsKnownType(Arg.Any<string>()).Returns(false);

        var resources = new[]
        {
            Resource("WidgetBase", "http://example.org/StructureDefinition/WidgetBase", BaseSd),
            Resource("WidgetProfile", "http://example.org/StructureDefinition/WidgetProfile", DifferentialOnlyProfile),
        };

        var provider = new ProfileLayeredSchemaProvider(baseProvider, resources);

        var profile = provider.GetTypeDefinition("WidgetProfile");
        profile.ShouldNotBeNull();
        var label = profile!.Children.Single(c => c.Info.Name == "label");
        label.IsRequired.ShouldBeTrue("differential tightened Widget.label to min=1");
        ((ITypeExtended)label).Min.ShouldBe(1);
    }

    private static ExtractedResource Resource(string id, string canonical, string json) => new()
    {
        ResourceType = "StructureDefinition",
        Canonical = canonical,
        ResourceId = id,
        ResourceJson = json,
        FhirVersion = "4.0.1",
    };
}
