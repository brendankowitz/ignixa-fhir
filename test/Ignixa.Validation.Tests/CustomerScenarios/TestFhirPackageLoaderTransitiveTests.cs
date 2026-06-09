// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Tests.TestHelpers.Packages;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// Tests for <see cref="TestFhirPackageLoader.LoadWithDependenciesAsync"/> - the test-side
/// transitive package loader. Walks <c>PackageManifest.Dependencies</c> recursively until
/// the closure stabilises, deduplicating by package id, skipping <c>hl7.fhir.r4.core</c>
/// (it's served from the built-in <c>R4CoreSchemaProvider</c>, not from a downloaded tarball).
/// </summary>
[Trait("Category", "RequiresNetwork")]
public class TestFhirPackageLoaderTransitiveTests
{
    [Fact]
    public async Task GivenAuCoreRoot_WhenLoadingWithDependencies_ThenAllExpectedPackagesInClosure()
    {
        var closure = await TestFhirPackageLoader.LoadWithDependenciesAsync(
            "hl7.fhir.au.core", "1.0.0", CancellationToken.None);

        var ids = closure.Select(p => p.Manifest.Name).ToList();

        ids.ShouldContain("hl7.fhir.au.core");
        ids.ShouldContain("hl7.fhir.au.base");
        ids.ShouldContain("hl7.terminology.r4");
        ids.ShouldContain("hl7.fhir.uv.extensions.r4");
        // R4 core is provided by R4CoreSchemaProvider and never downloaded as a tarball.
        ids.ShouldNotContain("hl7.fhir.r4.core");
    }

    [Fact]
    public async Task GivenAuCoreRoot_WhenLoadingWithDependencies_ThenClosureContainsNoDuplicates()
    {
        var closure = await TestFhirPackageLoader.LoadWithDependenciesAsync(
            "hl7.fhir.au.core", "1.0.0", CancellationToken.None);

        var ids = closure.Select(p => p.Manifest.Name).ToList();
        ids.Count.ShouldBe(ids.Distinct(StringComparer.Ordinal).Count(),
            "Closure must deduplicate packages even if multiple deps reference the same id");
    }

    [Fact]
    public async Task GivenCarinBbRoot_WhenLoadingWithDependencies_ThenAuBaseNotIncluded()
    {
        // CARIN-BB does not depend on AU Base. Sanity check that we didn't accidentally
        // build a "load everything we've ever seen" cache.
        var closure = await TestFhirPackageLoader.LoadWithDependenciesAsync(
            "hl7.fhir.us.carin-bb", "2.1.0", CancellationToken.None);

        var ids = closure.Select(p => p.Manifest.Name).ToList();
        ids.ShouldContain("hl7.fhir.us.carin-bb");
        ids.ShouldNotContain("hl7.fhir.au.base");
        ids.ShouldNotContain("hl7.fhir.au.core");
    }

    [Fact]
    public async Task GivenAuCoreRoot_WhenLoadingWithDependencies_ThenRootIsFirstInClosure()
    {
        var closure = await TestFhirPackageLoader.LoadWithDependenciesAsync(
            "hl7.fhir.au.core", "1.0.0", CancellationToken.None);

        closure[0].Manifest.Name.ShouldBe("hl7.fhir.au.core",
            "Root package must be first so callers passing the closure to "
            + "PackageValidatorFactory.BuildR4 get the expected layering precedence.");
    }
}
