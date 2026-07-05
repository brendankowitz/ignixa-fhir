// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;
using Ignixa.FhirFakes.EdgeCases;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class EdgeCaseCatalogTests
{
    [Fact]
    public void GivenConcurrentRegistrations_WhenResolvingConcurrently_ThenAllStrategiesAreVisibleAndUncorrupted()
    {
        var catalog = new EdgeCaseCatalog();
        var strategies = Enumerable.Range(0, 100)
            .Select(i => new FakeEdgeCaseStrategy($"test.category-{i}"))
            .ToList();

        Parallel.ForEach(strategies, s => catalog.Register(s));
        Parallel.For(0, 20, _ => catalog.Resolve(null));

        var all = catalog.All();

        all.Count.ShouldBe(strategies.Count);
        all.Select(s => s.Category).ToHashSet().Count.ShouldBe(strategies.Count);
    }

    private sealed class FakeEdgeCaseStrategy(string category) : IEdgeCaseStrategy
    {
        public string Category { get; } = category;

        public EdgeCaseFamily Family => EdgeCaseFamily.Unicode;

        public ValidityIntent Intent => ValidityIntent.PreservesValidity;

        public bool CanApply(MutationTarget target) => throw new NotSupportedException();

        public MutationResult Apply(MutationTarget target, Randomizer rng) => throw new NotSupportedException();
    }

    [Fact]
    public void GivenDefaultCatalog_WhenListingAll_ThenRegistersUnicodeAndTemporalStrategies()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var all = catalog.All();

        all.ShouldContain(s => s.Family == EdgeCaseFamily.Unicode);
        all.ShouldContain(s => s.Family == EdgeCaseFamily.Temporal);
        all.Count.ShouldBe(16);
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

    [Fact]
    public void GivenUnknownSelector_WhenResolvingWithUnmatched_ThenEmptyResultAndUnmatchedReported()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["nonsense"], out var unmatched);

        resolved.ShouldBeEmpty();
        unmatched.ShouldContain("nonsense");
    }

    [Fact]
    public void GivenMixedSelectors_WhenResolving_ThenUnionOfMatchingStrategiesReturned()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["unicode", "temporal.leap-year"]);

        resolved.Count.ShouldBe(7);
        resolved.ShouldContain(s => s.Family == EdgeCaseFamily.Unicode);
        resolved.ShouldContain(s => s.Category == "temporal.leap-year");
    }

    [Fact]
    public void GivenWhitespaceAndEmptySelectors_WhenResolving_ThenAllStrategiesReturned()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve([" ", ""]);

        resolved.Count.ShouldBe(catalog.All().Count);
    }

    [Fact]
    public void GivenSpecificUnicodeCategory_WhenResolving_ThenExactlyThatOneStrategy()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["unicode.rtl"]);

        resolved.Count.ShouldBe(1);
        resolved[0].Category.ShouldBe("unicode.rtl");
        resolved[0].Family.ShouldBe(EdgeCaseFamily.Unicode);
    }

    [Fact]
    public void GivenSpecificTemporalCategory_WhenResolving_ThenExactlyThatOneStrategy()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["temporal.far-future"]);

        resolved.Count.ShouldBe(1);
        resolved[0].Category.ShouldBe("temporal.far-future");
    }

    [Fact]
    public void GivenTemporalFamilySelector_WhenResolving_ThenOnlyTemporalStrategiesReturned()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["temporal"]);

        resolved.ShouldNotBeEmpty();
        resolved.ShouldAllBe(s => s.Family == EdgeCaseFamily.Temporal);
    }

    [Fact]
    public void GivenUnknownSelector_WhenResolvingWithoutUnmatched_ThenEmptyResultReturned()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["totally-unknown"]);

        resolved.ShouldBeEmpty();
    }
}
