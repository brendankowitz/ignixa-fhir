// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class PackageTerminologySourceTests
{
    private const string Local = "http://example.org/cs";
    private const string Snomed = "http://snomed.info/sct";

    private static PackageTerminologySource Create(ICodeSystemProvider? codeSystems = null, IValueSetProvider? valueSets = null) =>
        new(valueSets ?? Substitute.For<IValueSetProvider>(), codeSystems);

    [Fact]
    public async Task GivenEnumeratedCode_WhenLookedUp_ThenFoundWithProviderDisplay()
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Local, "car").Returns(true);
        codeSystems.GetDisplay(Local, "car").Returns("Car");

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "car"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.True);
        lookup.Concept!.Display.ShouldBe("Car");
    }

    [Fact]
    public async Task GivenEnumerableSystemWithoutTheCode_WhenLookedUp_ThenNotFound()
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Local, "boat").Returns(false);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "boat"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.False);
    }

    [Fact]
    public async Task GivenNonEnumerableSystemMiss_WhenLookedUp_ThenUnknownNonEnumerable()
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Snomed, "123").Returns(false);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Snomed, "123"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.Unknown);
        lookup.Unknown!.Kind.ShouldBe(UnknownKind.NonEnumerableSystem);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenSystemUnknownToProvider_WhenLookedUp_ThenUnknownSystemNotAvailable(bool hasProvider)
    {
        var codeSystems = hasProvider ? Substitute.For<ICodeSystemProvider>() : null;
        codeSystems?.ContainsCode(Local, "car").Returns((bool?)null);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "car"), CancellationToken.None);

        lookup.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
    }

    [Fact]
    public async Task GivenAnyCodes_WhenSubsumesAsked_ThenUndeterminedNeverNotSubsumed()
    {
        var outcome = await Create().SubsumesAsync(new CodeRef(Local, "a"), new CodeRef(Local, "b"), CancellationToken.None);

        outcome.ShouldBeOfType<SubsumesOutcome.Undetermined>();
    }

    [Theory]
    [InlineData(CanonicalKind.CodeSystem, false, SourceCoverage.Partial)]
    [InlineData(CanonicalKind.ValueSet, true, SourceCoverage.Authoritative)]
    [InlineData(CanonicalKind.ValueSet, false, SourceCoverage.None)]
    [InlineData(CanonicalKind.ConceptMap, true, SourceCoverage.None)]
    public async Task GivenCanonicalKind_WhenCoverageAsked_ThenSpecRuleApplies(CanonicalKind kind, bool knownValueSet, SourceCoverage expected)
    {
        var valueSets = Substitute.For<IValueSetProvider>();
        valueSets.IsKnownValueSet("http://example.org/x").Returns(knownValueSet);

        var coverage = await Create(valueSets: valueSets).GetCoverageAsync(kind, new CanonicalRef("http://example.org/x"), CancellationToken.None);

        coverage.ShouldBe(expected);
    }
}
