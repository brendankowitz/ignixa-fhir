// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;

namespace Ignixa.Search.Tests.Models;

/// <summary>
/// Regressions for <see cref="PartialDateTime.ToString"/>, which index caches and sidecars persist and parse back: at
/// 206143a it printed negative offsets with minutes as <c>-05:-30</c> and years below 1000 unpadded, neither of which
/// <see cref="PartialDateTime.Parse"/> accepts.
/// </summary>
public class PartialDateTimeTests
{
    [Theory]
    [InlineData("2020-01-02T03:04:05-05:30")]
    [InlineData("2020-01-02T03:04-00:30")]
    [InlineData("2020-01-02T03:04:05-09:45")]
    [InlineData("0001")]
    [InlineData("0999-12")]
    [InlineData("0042-03-04")]
    [InlineData("0500-06-07T08:09:10.25+01:00")]
    public void GivenAPartialDateTime_WhenPrintedAndParsedBack_ThenItIsTheSameValueAndText(string text)
    {
        PartialDateTime parsed = PartialDateTime.Parse(text);

        PartialDateTime reparsed = PartialDateTime.Parse(parsed.ToString());

        reparsed.ToString().ShouldBe(parsed.ToString());
        (reparsed.Year, reparsed.Month, reparsed.Day, reparsed.Hour, reparsed.Minute, reparsed.Second, reparsed.Fraction, reparsed.UtcOffset)
            .ShouldBe((parsed.Year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, parsed.Second, parsed.Fraction, parsed.UtcOffset));
    }

    [Theory]
    [InlineData("2020-01-02T03:04:05-05:30", "2020-01-02T03:04:05-05:30")]
    [InlineData("2020-01-02T03:04-00:30", "2020-01-02T03:04-00:30")]
    [InlineData("0042-03-04", "0042-03-04")]
    public void GivenANegativeHalfHourOffsetOrASmallYear_WhenPrinted_ThenTheTextIsTheFhirForm(string text, string expected)
    {
        PartialDateTime.Parse(text).ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("2020-01-02T03:04:05+05:30", "2020-01-02T03:04:05+05:30")]
    [InlineData("2020-01-02T03:04:05Z", "2020-01-02T03:04:05+00:00")]
    [InlineData("2020-01-02T03:04:05-05:00", "2020-01-02T03:04:05-05:00")]
    [InlineData("2020", "2020")]
    public void GivenAPositiveOrWholeHourOffsetOrAFourDigitYear_WhenPrinted_ThenTheTextIsUnchanged(string text, string expected)
    {
        // Guard: these printed correctly before the fix.
        PartialDateTime.Parse(text).ToString().ShouldBe(expected);
    }
}
