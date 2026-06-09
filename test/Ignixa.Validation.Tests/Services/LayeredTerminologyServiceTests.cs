// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Services;
using Ignixa.Validation.Tests.TestHelpers.Packages;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Services;

/// <summary>
/// Integration tests for the layered <see cref="InMemoryTerminologyService"/> constructor
/// that accepts additional <see cref="IValueSetProvider"/> sources on top of the base provider.
/// Exercises end-to-end with a real CARIN BlueButton package supplying IG-defined ValueSets.
/// </summary>
[Trait("Category", "RequiresNetwork")]
public class LayeredTerminologyServiceTests
{
    [Fact]
    public async Task GivenPackageValueSetSource_WhenValidatingCarinBbValueSet_ThenResolvesViaPackage()
    {
        var pkg = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);
        var packageSource = new PackageValueSetSource(pkg.Resources);
        var service = new InMemoryTerminologyService(
            primary: new R4CoreSchemaProvider().ValueSetProvider,
            additional: new[] { (IValueSetProvider)packageSource });

        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/us/carin-bb/CodeSystem/C4BBAdjudication",
            code: "paidtoprovider",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/us/carin-bb/ValueSet/C4BBAdjudication",
            cancellationToken: CancellationToken.None);

        // The package source should resolve the CARIN-BB ValueSet, so we get a definitive
        // answer (either valid or invalid code), not "unknown ValueSet".
        result.Severity.ShouldNotBe(IssueSeverity.Warning,
            $"Expected definitive answer for CARIN-BB ValueSet, got warning: {result.Message}");
    }

    [Fact]
    public async Task GivenPackageValueSetSource_WhenValidatingBaseSpecValueSet_ThenFallsBackToPrimary()
    {
        // CARIN-BB doesn't define http://hl7.org/fhir/ValueSet/administrative-gender;
        // the lookup must fall through to the R4 base-spec provider.
        var pkg = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(CancellationToken.None);
        var packageSource = new PackageValueSetSource(pkg.Resources);
        var service = new InMemoryTerminologyService(
            primary: new R4CoreSchemaProvider().ValueSetProvider,
            additional: new[] { (IValueSetProvider)packageSource });

        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: "male",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            cancellationToken: CancellationToken.None);

        result.IsValid.ShouldBeTrue($"Base-spec administrative-gender#male should validate; got: {result.Message}");
    }

    [Fact]
    public async Task GivenLayeredService_WhenConstructedWithEmptyAdditional_ThenBehavesAsBaseProvider()
    {
        var service = new InMemoryTerminologyService(
            primary: new R4CoreSchemaProvider().ValueSetProvider,
            additional: Array.Empty<IValueSetProvider>());

        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: "male",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            cancellationToken: CancellationToken.None);

        result.IsValid.ShouldBeTrue("empty-additional constructor should fall back to primary provider");
        result.Severity.ShouldNotBe(IssueSeverity.Warning, "known VS with valid code should not produce a warning");
    }
}
