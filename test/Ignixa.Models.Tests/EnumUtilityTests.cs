// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

/// <summary>
/// Pins <see cref="EnumUtility"/>'s behavior around codes that are valid in one FHIR version but not the
/// enum being parsed against. This is a deliberate, tested contract -- not an accident: an enum-bound
/// element only demotes to per-version <c>Incompatible</c> when its value-set CODE SET actually differs
/// (see <c>ClassificationLockTests.GivenValueSetThatGainedCodesUnderTheSameUrl...</c>); a genuinely
/// unrecognized literal for the enum in hand still has nowhere sensible to go but <c>null</c>.
/// </summary>
public sealed class EnumUtilityTests
{
    [Fact]
    public void GivenLiteralNotInTheEnum_WhenParsed_ThenReturnsNull()
    {
        // "ad" is a valid R5 QuantityComparator code (added in R5) but is not a member of the R4 enum.
        Ignixa.Models.R4.QuantityComparator? parsed =
            EnumUtility.ParseLiteral<Ignixa.Models.R4.QuantityComparator>("ad");

        parsed.ShouldBeNull();
    }

    [Fact]
    public void GivenLiteralValidForTheVersion_WhenParsed_ThenReturnsTheMatchingValue()
    {
        Ignixa.Models.R5.QuantityComparator? parsed =
            EnumUtility.ParseLiteral<Ignixa.Models.R5.QuantityComparator>("ad");

        parsed.ShouldBe(Ignixa.Models.R5.QuantityComparator.Ad);
    }

    [Fact]
    public void GivenNullOrWhitespaceLiteral_WhenParsed_ThenReturnsNull()
    {
        EnumUtility.ParseLiteral<Ignixa.Models.R4.QuantityComparator>(null).ShouldBeNull();
        EnumUtility.ParseLiteral<Ignixa.Models.R4.QuantityComparator>("   ").ShouldBeNull();
    }

    [Fact]
    public void GivenEnumValueWithNullSet_WhenWrittenThroughGeneratedAccessor_ThenJsonKeyIsRemoved()
    {
        var quantity = new Ignixa.Models.R4.Quantity(new JsonObject());
        quantity.Comparator = Ignixa.Models.R4.QuantityComparator.LessThan;
        quantity.MutableNode["comparator"].ShouldNotBeNull();

        quantity.Comparator = null;

        quantity.MutableNode["comparator"].ShouldBeNull();
        quantity.Comparator.ShouldBeNull();
    }
}
