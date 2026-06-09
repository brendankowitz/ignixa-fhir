// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Tests.TestHelpers.Packages;
using Shouldly;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// Verifies that <see cref="TestFhirPackageLoader"/> can download, cache and extract the
/// CARIN BlueButton (<c>hl7.fhir.us.carin-bb</c>) IG package, and that the package contains
/// the StructureDefinitions and ValueSets needed to validate the customer's
/// <see cref="CarinBbExplanationOfBenefitScenarioTests">CARIN-BB EOB scenario</see>.
/// <para>
/// These tests intentionally exercise the real network on first run and the on-disk cache on
/// subsequent runs. Cache directory: see <see cref="TestFhirPackageLoader.GetCacheDirectory"/>.
/// </para>
/// </summary>
[Trait("Category", "RequiresNetwork")]
public class CarinBlueButtonPackageTests
{
    [Fact]
    public async Task GivenCarinBluebuttonPackage_WhenLoaded_ThenManifestMatchesExpectedVersion()
    {
        var package = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);

        package.Manifest.Name.ShouldBe("hl7.fhir.us.carin-bb");
        package.Manifest.Version.ShouldBe("2.1.0");
        package.Manifest.FhirVersion.ShouldStartWith("4.0", Case.Sensitive);
    }

    [Fact]
    public async Task GivenCarinBluebuttonPackage_WhenLoaded_ThenExposesC4BbInpatientInstitutionalProfile()
    {
        // The customer's EOB fixture declares meta.profile = this canonical (with |2.1.0 suffix).
        const string profileCanonical = "http://hl7.org/fhir/us/carin-bb/StructureDefinition/C4BB-ExplanationOfBenefit-Inpatient-Institutional";

        var package = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);

        var profile = package.FindByCanonical(profileCanonical);
        profile.ShouldNotBeNull($"Profile '{profileCanonical}' should be present in CARIN-BB {package.Manifest.Version}");
        profile.ResourceType.ShouldBe("StructureDefinition");
        profile.ResourceId.ShouldBe("C4BB-ExplanationOfBenefit-Inpatient-Institutional");
        profile.ResourceJson.ShouldContain("\"resourceType\"", Case.Sensitive);
        profile.ResourceJson.ShouldContain("\"StructureDefinition\"", Case.Sensitive);
        profile.ResourceJson.ShouldContain("ExplanationOfBenefit", Case.Sensitive);
    }

    [Fact]
    public async Task GivenCarinBluebuttonPackage_WhenLoaded_ThenContainsCarinBbValueSetsCustomerScenarioReferences()
    {
        // The legacy Firely OperationOutcome cites these CARIN-BB ValueSets explicitly.
        // Loading them is a prerequisite for profile-aware binding validation.
        var expectedValueSets = new[]
        {
            "http://hl7.org/fhir/us/carin-bb/ValueSet/AHANUBCRevenueCodes",
            "http://hl7.org/fhir/us/carin-bb/ValueSet/C4BBPayerClaimPaymentStatusCode",
            "http://hl7.org/fhir/us/carin-bb/ValueSet/C4BBAdjudication",
        };

        var package = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);

        foreach (var canonical in expectedValueSets)
        {
            var valueSet = package.FindByCanonical(canonical);
            valueSet.ShouldNotBeNull($"ValueSet '{canonical}' should be present in CARIN-BB {package.Manifest.Version}");
            valueSet.ResourceType.ShouldBe("ValueSet");
        }
    }

    [Fact]
    public async Task GivenCarinBluebuttonPackage_WhenLoadedTwice_ThenReturnsSameInstance()
    {
        // Memoization contract: the loader caches per (packageId, version) so tests can
        // share the extraction cost (~700KB tarball + JSON parsing across hundreds of files).
        var first = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);
        var second = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);

        ReferenceEquals(first, second).ShouldBeTrue("Loader must memoize the extracted package per (packageId, version)");
    }

    [Fact]
    public async Task GivenCarinBluebuttonPackage_WhenIndexingByResourceType_ThenContainsStructureDefinitionsAndValueSets()
    {
        var package = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);

        package.OfResourceType("StructureDefinition").ShouldNotBeEmpty();
        package.OfResourceType("ValueSet").ShouldNotBeEmpty();
        package.OfResourceType("CodeSystem").ShouldNotBeEmpty();
    }
}
