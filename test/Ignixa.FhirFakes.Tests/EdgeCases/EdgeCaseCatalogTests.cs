// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.EdgeCases;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class EdgeCaseCatalogTests
{
    [Fact]
    public void GivenDefaultCatalog_WhenListingAll_ThenRegistersUnicodeAndTemporalStrategies()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var all = catalog.All();

        all.ShouldContain(s => s.Family == EdgeCaseFamily.Unicode);
        all.ShouldContain(s => s.Family == EdgeCaseFamily.Temporal);
        all.Count.ShouldBe(11);
    }

    [Fact]
    public void GivenDefaultCatalog_WhenResolvingUnicodeFamily_ThenReturnsOnlyUnicodeStrategies()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["unicode"]);

        resolved.ShouldNotBeEmpty();
        resolved.ShouldAllBe(s => s.Family == EdgeCaseFamily.Unicode);
    }

    [Fact]
    public void GivenDefaultCatalog_WhenResolvingSpecificCategory_ThenReturnsExactlyThatStrategy()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["temporal.leap-year"]);

        resolved.Count.ShouldBe(1);
        resolved[0].Category.ShouldBe("temporal.leap-year");
    }

    [Fact]
    public void GivenDefaultCatalog_WhenResolvingNull_ThenReturnsAllStrategies()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(null);

        resolved.Count.ShouldBe(catalog.All().Count);
    }

    [Fact]
    public void GivenDefaultCatalog_WhenResolvingEmpty_ThenReturnsAllStrategies()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve([]);

        resolved.Count.ShouldBe(catalog.All().Count);
    }

    [Fact]
    public void GivenDefaultCatalog_WhenResolvingCaseInsensitiveFamily_ThenMatches()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["UNICODE"]);

        resolved.ShouldAllBe(s => s.Family == EdgeCaseFamily.Unicode);
        resolved.ShouldNotBeEmpty();
    }
}
