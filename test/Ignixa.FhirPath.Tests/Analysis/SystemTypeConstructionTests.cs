// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Analysis;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Pins the unknown state of <see cref="SystemTypeConstruction"/>, which had no coverage at all.
/// </summary>
/// <remarks>
/// The unknown state exists so that a construction the analysis cannot enumerate fails loudly instead
/// of degrading into a confident, wrong answer. An empty <c>TypeNames</c> would read downstream as
/// "constructs nothing", which collapses to <c>AlwaysEmpty = true</c> - a false always-empty verdict,
/// the one direction this analysis declares dangerous. Nothing enforced that; these tests do.
/// </remarks>
public class SystemTypeConstructionTests
{
    private const string KnownEmpty = "the empty construction";
    private const string NoSystemTypes = "a construction with no System types";
    private const string Numeric = "the numeric construction";
    private const string SingleNamedType = "a single named type";

    [Fact]
    public void GivenUnknownConstruction_WhenTypeNamesRead_ThenThrows() =>
        Should.Throw<InvalidOperationException>(() => SystemTypeConstruction.Any.TypeNames);

    [Fact]
    public void GivenKnownConstruction_WhenTypeNamesRead_ThenEnumerates() =>
        SystemTypeConstruction.For("integer").TypeNames.ShouldBe(["integer"]);

    [Theory]
    [MemberData(nameof(KnownConstructions))]
    public void GivenUnknownConstructionUnionedWithAKnownOne_WhenCombined_ThenStaysUnknown(string caseName)
    {
        // Arrange
        var known = ResolveKnownConstruction(caseName);

        // Act
        var unionedAfter = SystemTypeConstruction.Any.Union(known);
        var unionedBefore = known.Union(SystemTypeConstruction.Any);

        // Assert
        unionedAfter.MayConstructAny.ShouldBeTrue(
            $"union with {caseName} must not resolve the unknown state, or a branch the analysis cannot "
            + "enumerate is silently absorbed into one it can.");
        unionedBefore.MayConstructAny.ShouldBeTrue(
            $"{caseName} unioned with the unknown state must stay unknown regardless of operand order.");
    }

    [Fact]
    public void GivenUnknownConstruction_WhenNegated_ThenStaysUnknown() =>
        SystemTypeConstruction.Any.Negate().MayConstructAny.ShouldBeTrue(
            "negating an unenumerable construction cannot make it enumerable; collapsing here would let "
            + "unary minus manufacture a confident empty type set.");

    [Fact]
    public void GivenConstructionOfATypeNegationDoesNotKnow_WhenNegated_ThenBecomesUnknown() =>
        SystemTypeConstruction.For("string").Negate().MayConstructAny.ShouldBeTrue(
            "a type name the negation rule has not been taught must widen to unknown, not to nothing.");

    [Fact]
    public void GivenEmptyConstruction_WhenNegated_ThenStaysKnownEmpty()
    {
        // Act
        var negated = SystemTypeConstruction.Empty.Negate();

        // Assert
        negated.IsKnownEmpty.ShouldBeTrue();
        negated.MayConstructAny.ShouldBeFalse(
            "a construction known to be empty is not unknown, and widening it would discard the only "
            + "always-empty verdict this type can soundly supply.");
    }

    public static TheoryData<string> KnownConstructions() =>
        new() { KnownEmpty, NoSystemTypes, Numeric, SingleNamedType };

    private static SystemTypeConstruction ResolveKnownConstruction(string caseName) =>
        caseName switch
        {
            KnownEmpty => SystemTypeConstruction.Empty,
            NoSystemTypes => SystemTypeConstruction.None,
            Numeric => SystemTypeConstruction.Numeric,
            SingleNamedType => SystemTypeConstruction.For("integer"),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unmapped case."),
        };
}
