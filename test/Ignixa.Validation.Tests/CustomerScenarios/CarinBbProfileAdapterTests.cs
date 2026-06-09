// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Validation.Tests.TestHelpers.Packages;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// End-to-end coverage for <see cref="PackageResourceProvider"/> wired against the real
/// CARIN BlueButton 2.1.0 IG package. Validates that loading a profile, converting it to
/// <see cref="IType"/>, and inspecting profile-defined invariants/bindings all succeed.
/// </summary>
[Trait("Category", "RequiresNetwork")]
public class CarinBbProfileAdapterTests
{
    private const string ProfileCanonical =
        "http://hl7.org/fhir/us/carin-bb/StructureDefinition/C4BB-ExplanationOfBenefit-Inpatient-Institutional";

    [Fact]
    public async Task GivenCarinBbInpatientProfile_WhenAdapted_ThenRootIsExplanationOfBenefit()
    {
        var pkg = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);
        var sd = pkg.FindByCanonical(ProfileCanonical);
        sd.ShouldNotBeNull();

        var provider = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);

        var type = provider.ToTypeDefinition(sd.ResourceJson, "4.0.1");

        type.ShouldNotBeNull();
        type!.Info.Name.ShouldBe("ExplanationOfBenefit");
        type.Info.IsResource.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenCarinBbInpatientProfile_WhenAdapted_ThenCareTeamCarriesEobInstCareTeamPractitionerConstraint()
    {
        var pkg = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);
        var sd = pkg.FindByCanonical(ProfileCanonical);
        sd.ShouldNotBeNull();

        var provider = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);
        var root = (ITypeExtended)provider.ToTypeDefinition(sd.ResourceJson, "4.0.1")!;

        var careTeam = root.Children.SingleOrDefault(c => c.Info.Name == "careTeam");
        careTeam.ShouldNotBeNull("CARIN-BB EOB profile must expose a careTeam child element");

        var careTeamExtended = (ITypeExtended)careTeam!;
        careTeamExtended.Constraints.ShouldContain(
            c => c.Key == "EOB-inst-careTeam-practitioner",
            customMessage: "Adapter must surface the CARIN-BB careTeam profile invariant");
    }

    [Fact]
    public async Task GivenCarinBbInpatientProfile_WhenAdapted_ThenItemCarriesAdjudicationHasAmountTypeSliceConstraint()
    {
        var pkg = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);
        var sd = pkg.FindByCanonical(ProfileCanonical);
        sd.ShouldNotBeNull();

        var provider = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);
        var root = (ITypeExtended)provider.ToTypeDefinition(sd.ResourceJson, "4.0.1")!;

        var item = root.Children.SingleOrDefault(c => c.Info.Name == "item");
        item.ShouldNotBeNull();
        var itemExtended = (ITypeExtended)item!;

        itemExtended.Constraints.ShouldContain(c => c.Key == "adjudication-has-amount-type-slice");
    }
}
