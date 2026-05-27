// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Tests.TestHelpers.Packages;
using Shouldly;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// Verifies that <see cref="TestFhirPackageLoader.LoadUsCoreAsync"/> downloads, caches and
/// extracts the US Core (<c>hl7.fhir.us.core</c>) IG package, and that the package contains
/// the StructureDefinitions and extensions referenced by US Core profiles.
/// <para>
/// First run downloads ~1.6 MB from <c>packages.fhir.org</c>; subsequent runs hit the local cache.
/// </para>
/// </summary>
public class UsCorePackageTests
{
    [Fact]
    public async Task GivenUsCorePackage_WhenLoaded_ThenManifestMatchesExpectedVersion()
    {
        var package = await TestFhirPackageLoader.LoadUsCoreAsync(CancellationToken.None);

        package.Manifest.Name.ShouldBe("hl7.fhir.us.core");
        package.Manifest.Version.ShouldBe("6.1.0");
        package.Manifest.FhirVersion.ShouldStartWith("4.0", Case.Sensitive);
    }

    [Fact]
    public async Task GivenUsCorePackage_WhenLoaded_ThenExposesUsCorePatientProfile()
    {
        const string profileCanonical = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient";

        var package = await TestFhirPackageLoader.LoadUsCoreAsync(CancellationToken.None);
        var profile = package.FindByCanonical(profileCanonical);

        profile.ShouldNotBeNull($"Profile '{profileCanonical}' should be present in US Core {package.Manifest.Version}");
        profile.ResourceType.ShouldBe("StructureDefinition");
        profile.ResourceId.ShouldBe("us-core-patient");
    }

    [Fact]
    public async Task GivenUsCorePackage_WhenLoaded_ThenContainsRaceEthnicityAndBirthsexExtensions()
    {
        var package = await TestFhirPackageLoader.LoadUsCoreAsync(CancellationToken.None);

        var requiredExtensions = new[]
        {
            "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race",
            "http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity",
            "http://hl7.org/fhir/us/core/StructureDefinition/us-core-birthsex",
        };

        foreach (var canonical in requiredExtensions)
        {
            var ext = package.FindByCanonical(canonical);
            ext.ShouldNotBeNull($"Extension '{canonical}' should be present in US Core {package.Manifest.Version}");
            ext.ResourceType.ShouldBe("StructureDefinition");
        }
    }

    [Fact]
    public async Task GivenUsCorePackage_WhenIndexingByResourceType_ThenContainsExpectedConformanceShapes()
    {
        var package = await TestFhirPackageLoader.LoadUsCoreAsync(CancellationToken.None);

        package.OfResourceType("StructureDefinition").ShouldNotBeEmpty();
        package.OfResourceType("ValueSet").ShouldNotBeEmpty();
        package.OfResourceType("CodeSystem").ShouldNotBeEmpty();
    }
}
