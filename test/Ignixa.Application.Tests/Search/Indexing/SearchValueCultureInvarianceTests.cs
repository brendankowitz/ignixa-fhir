// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using Ignixa.Application.Tests.Search.Parsing;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Pins the string form of the numeric and temporal search values to the invariant culture.
/// </summary>
/// <remarks>
/// Measured before the fix: <c>NumberSearchValue(1.5m, 2.5m).ToString()</c> produced <c>[1,5, 2,5)</c> on
/// de-DE and fr-FR, and <c>[1٫5, 2٫5)</c> on ar-SA (U+066B ARABIC DECIMAL SEPARATOR). A negative low bound
/// additionally picked up U+061C ARABIC LETTER MARK ahead of the minus sign on ar-SA, so the sign is
/// covered as well as the separator.
///
/// Scope, deliberately stated because it is narrower than it looks: these <c>ToString()</c> overrides are
/// NOT on the persistence path. The SQL row generators and <c>CompactSearchValueWriter</c> both write the
/// typed <c>Low</c>/<c>High</c>/<c>Start</c>/<c>End</c> properties, and nothing re-renders a search value
/// back into a FHIR search literal. What the locale actually reached was diagnostic and parity output -
/// <c>SearchParameterPredicateExpression.ToString()</c>, the IR trace rows, and composite value rendering.
/// So this is a latent defect made safe, not stored data repaired; the fix matters because these are public
/// API whose string form is a documented contract, and because locale-dependent trace output makes parity
/// diffs unreproducible between machines.
///
/// The equal-bounds branch of <see cref="NumberSearchValue"/> was already invariant; only the range branch
/// was missed. The tests cover both so the asymmetry cannot come back.
/// </remarks>
public class SearchValueCultureInvarianceTests
{
    public static TheoryData<string> CorruptingCultures =>
        new() { "de-DE", "fr-FR", "ar-SA", "th-TH", "en-US" };

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenANumberRange_WhenTheHostCultureVaries_ThenTheStringFormStaysInvariant(string cultureName)
    {
        // Arrange
        var value = new NumberSearchValue(1.5m, 2.5m);

        // Act
        string result = UnderCulture(cultureName, value.ToString);

        // Assert
        result.ShouldBe("[1.5, 2.5)");
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenANumberRangeSpanningTheGroupingThreshold_WhenTheHostCultureVaries_ThenNoGroupingOrSeparatorSwapLeaksIn(string cultureName)
    {
        // Arrange - de-DE swaps '.' and ',' at exactly this magnitude, so a grouping leak is unambiguous.
        var value = new NumberSearchValue(1234567.89m, 2345678.91m);

        // Act
        string result = UnderCulture(cultureName, value.ToString);

        // Assert
        result.ShouldBe("[1234567.89, 2345678.91)");
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenANegativeNumberRange_WhenTheHostCultureVaries_ThenTheSignStaysAsciiHyphenMinus(string cultureName)
    {
        // Arrange - ar-SA prefixes its NegativeSign with U+061C ARABIC LETTER MARK.
        var value = new NumberSearchValue(-1.5m, 2.5m);

        // Act
        string result = UnderCulture(cultureName, value.ToString);

        // Assert
        result.ShouldBe("[-1.5, 2.5)");
        result.ShouldNotContain('؜');
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenASingleValuedNumber_WhenTheHostCultureVaries_ThenTheStringFormStaysInvariant(string cultureName)
    {
        // Arrange
        var value = new NumberSearchValue(1.5m);

        // Act
        string result = UnderCulture(cultureName, value.ToString);

        // Assert
        result.ShouldBe("1.5");
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenASingleValuedQuantity_WhenTheHostCultureVaries_ThenTheStringFormStaysInvariant(string cultureName)
    {
        // Arrange
        var value = new QuantitySearchValue("http://unitsofmeasure.org", "mg", 5.4m);

        // Act
        string result = UnderCulture(cultureName, value.ToString);

        // Assert
        result.ShouldBe("5.4|http://unitsofmeasure.org|mg");
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenAQuantityRange_WhenTheHostCultureVaries_ThenTheStringFormStaysInvariant(string cultureName)
    {
        // Arrange
        var value = new QuantitySearchValue("http://unitsofmeasure.org", "mg", 5.4m, 7.8m);

        // Act
        string result = UnderCulture(cultureName, value.ToString);

        // Assert
        result.ShouldBe("[5.4,7.8)|http://unitsofmeasure.org|mg");
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenANegativeQuantityRange_WhenTheHostCultureVaries_ThenTheSignStaysAsciiHyphenMinus(string cultureName)
    {
        // Arrange
        var value = new QuantitySearchValue(null, "mg", -5.4m, 7.8m);

        // Act
        string result = UnderCulture(cultureName, value.ToString);

        // Assert
        result.ShouldBe("[-5.4,7.8)||mg");
        result.ShouldNotContain('؜');
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenAQuantityParsedFromAWireLiteral_WhenTheHostCultureVaries_ThenItRoundTrips(string cultureName)
    {
        // Arrange & Act - parse and render both under the foreign culture.
        string result = UnderCulture(
            cultureName,
            () => QuantitySearchValue.Parse("5.4|http://unitsofmeasure.org|mg").ToString());

        // Assert
        result.ShouldBe("5.4|http://unitsofmeasure.org|mg");
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenAFractionalSecond_WhenTheHostCultureVaries_ThenThePartialDateTimeLiteralStaysInvariant(string cultureName)
    {
        // Arrange - the fractional-second branch is the one that emitted ':05,1234567' on de-DE.
        const string Literal = "2013-01-15T05:30:05.1234567+02:00";

        // Act
        string result = UnderCulture(cultureName, () => PartialDateTime.Parse(Literal).ToString());

        // Assert
        result.ShouldBe(Literal);
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenAFractionalSecond_WhenTheHostCultureVaries_ThenTheDateTimeSearchValueLiteralStaysInvariant(string cultureName)
    {
        // Arrange - DateTimeSearchValue delegates to PartialDateTime for the original-date form.
        const string Literal = "2013-01-15T05:30:05.1234567+02:00";

        // Act
        string result = UnderCulture(cultureName, () => DateTimeSearchValue.Parse(Literal).ToString());

        // Assert
        result.ShouldBe(Literal);
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenANegativeUtcOffset_WhenTheHostCultureVaries_ThenTheOffsetSignStaysAsciiHyphenMinus(string cultureName)
    {
        // Arrange - the offset uses a literal '-' in a custom format section rather than NegativeSign,
        // so this already measured clean; the test pins that it stays that way.
        const string Literal = "2013-01-15T05:30:05-05:00";

        // Act
        string result = UnderCulture(cultureName, () => PartialDateTime.Parse(Literal).ToString());

        // Assert
        result.ShouldBe(Literal);
        result.ShouldNotContain('؜');
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenADateOnlyLiteral_WhenTheHostCultureVaries_ThenNoAlternateCalendarLeaksIn(string cultureName)
    {
        // Arrange - th-TH's default calendar is Buddhist (2013 -> 2556) and ar-SA's is UmAlQura. The
        // emitter formats the already-extracted integer components, so no calendar conversion applies.
        const string Literal = "2013-01-15";

        // Act
        string result = UnderCulture(cultureName, () => PartialDateTime.Parse(Literal).ToString());

        // Assert
        result.ShouldBe(Literal);
    }

    [Fact]
    public void GivenANegativeCountValue_WhenBuildingSearchOptionsUnderArSaCulture_ThenTheNonNegativeMessageIsThrown()
    {
        // Arrange - ar-SA's NegativeSign is U+061C followed by '-', and .NET does NOT fall back to a bare
        // ASCII '-' for the leading sign, so a provider-less int.TryParse("-5") returns false there while
        // succeeding everywhere else. SearchOptionsBuilder.cs:128 must parse with InvariantCulture so "-5"
        // parses cleanly under ar-SA too - if it regressed to the provider-less overload, the parse itself
        // would fail here and the wrong exception message (":130-133", "not a valid integer") would fire
        // instead of the real defect this exercises (":135-139", "non-negative integer").
        var harness = SearchOptionsBuilderHarness.ForPatient();

        // Act
        BadSearchRequestException exception = UnderCulture(
            "ar-SA",
            () => Should.Throw<BadSearchRequestException>(() => harness.Build([("_count", "-5")])));

        // Assert
        exception.Message.ShouldContain("must be a non-negative integer");
        exception.Message.ShouldNotContain("is not a valid integer");
    }

    [Fact]
    public void GivenAPositiveCountValue_WhenBuildingSearchOptionsUnderArSaCulture_ThenMaxItemCountIsSet()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient();

        // Act
        SearchOptions options = UnderCulture("ar-SA", () => harness.Build([("_count", "5")]));

        // Assert
        options.MaxItemCount.ShouldBe(5);
    }

    [Fact]
    public void GivenANegativeIncludesCountValue_WhenBuildingSearchOptionsUnderArSaCulture_ThenTheNonNegativeMessageIsThrown()
    {
        // Arrange - the same locale hazard as _count, but for _includesCount (SearchOptionsBuilder.cs:216),
        // which had zero coverage of any kind before this.
        var harness = SearchOptionsBuilderHarness.ForPatient();

        // Act
        BadSearchRequestException exception = UnderCulture(
            "ar-SA",
            () => Should.Throw<BadSearchRequestException>(() => harness.Build([("_includesCount", "-5")])));

        // Assert
        exception.Message.ShouldContain("must be a non-negative integer");
        exception.Message.ShouldNotContain("is not a valid integer");
    }

    [Fact]
    public void GivenAPositiveIncludesCountValue_WhenBuildingSearchOptionsUnderArSaCulture_ThenIncludesMaxItemCountIsSet()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient();

        // Act
        SearchOptions options = UnderCulture("ar-SA", () => harness.Build([("_includesCount", "5")]));

        // Assert
        options.IncludesMaxItemCount.ShouldBe(5);
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenAPatientEverythingDateFilter_WhenTheHostCultureVaries_ThenTheToStringDatesStayGregorianInvariant(string cultureName)
    {
        // Arrange - th-TH's default calendar is Buddhist (2026 -> 2569) and ar-SA's is UmAlQura
        // (2026-03-05 -> 1447-09-16). The custom "yyyy-MM-dd" format resolves against
        // CultureInfo.CurrentCulture.Calendar unless a provider pins it to invariant/Gregorian.
        var expression = new PatientEverythingExpression(
            "patient-1",
            startDate: new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
            endDate: new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));

        // Act
        string result = UnderCulture(cultureName, expression.ToString);

        // Assert
        result.ShouldBe("(PatientEverything 'patient-1' start=2026-03-05 end=2026-03-10)");
    }

    private static string UnderCulture(string cultureName, Func<string?> act)
    {
        return UnderCulture<string?>(cultureName, act)!;
    }

    private static T UnderCulture<T>(string cultureName, Func<T> act)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
