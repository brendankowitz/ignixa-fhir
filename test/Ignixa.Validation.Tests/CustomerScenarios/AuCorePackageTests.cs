// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Tests.TestHelpers.Packages;
using Shouldly;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// Verifies that <see cref="TestFhirPackageLoader"/> can download, cache and extract
/// AU Core 1.0.0 and its core transitive dependencies (AU Base, HL7 Terminology R4,
/// UV Extensions R4).
/// <para>
/// First run downloads ~10 MB; subsequent runs hit the local cache.
/// </para>
/// </summary>
public class AuCorePackageTests
{
    [Fact]
    public async Task GivenAuCorePackage_WhenLoaded_ThenManifestMatchesExpectedVersion()
    {
        var package = await TestFhirPackageLoader.LoadAuCoreAsync(CancellationToken.None);

        package.Manifest.Name.ShouldBe("hl7.fhir.au.core");
        package.Manifest.Version.ShouldBe("1.0.0");
        package.Manifest.FhirVersion.ShouldStartWith("4.0", Case.Sensitive);
    }

    [Fact]
    public async Task GivenAuCorePackage_WhenLoaded_ThenExposesAuCorePatientProfile()
    {
        const string profileCanonical = "http://hl7.org.au/fhir/core/StructureDefinition/au-core-patient";

        var package = await TestFhirPackageLoader.LoadAuCoreAsync(CancellationToken.None);
        var profile = package.FindByCanonical(profileCanonical);

        profile.ShouldNotBeNull($"Profile '{profileCanonical}' should be present in AU Core {package.Manifest.Version}");
        profile.ResourceType.ShouldBe("StructureDefinition");
        profile.ResourceId.ShouldBe("au-core-patient");
    }

    [Fact]
    public async Task GivenAuBasePackage_WhenLoaded_ThenContainsAuBasePatientProfile()
    {
        const string profileCanonical = "http://hl7.org.au/fhir/StructureDefinition/au-patient";

        var package = await TestFhirPackageLoader.LoadAuBaseAsync(CancellationToken.None);
        var profile = package.FindByCanonical(profileCanonical);

        profile.ShouldNotBeNull(
            $"AU Base must expose its core Patient profile ({profileCanonical}) - " +
            "AU Core Patient inherits from it.");
    }

    [Fact]
    public async Task GivenHl7TerminologyPackage_WhenLoaded_ThenContainsValueSets()
    {
        var package = await TestFhirPackageLoader.LoadHl7TerminologyR4Async(CancellationToken.None);

        package.Manifest.Name.ShouldBe("hl7.terminology.r4");
        package.OfResourceType("ValueSet").ShouldNotBeEmpty();
        package.OfResourceType("CodeSystem").ShouldNotBeEmpty();
    }
}
