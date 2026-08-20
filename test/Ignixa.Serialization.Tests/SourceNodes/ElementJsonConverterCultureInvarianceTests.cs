/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins the invariant that FHIR primitive values are WRITTEN to JSON independently of the
 * host culture. This is the output-side mirror of SchemaAwareElementCultureInvarianceTests.
 */

using System.Globalization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.Utilities;
using Xunit;

namespace Ignixa.Serialization.Tests.SourceNodes;

/// <summary>
/// Regression tests for culture-sensitive formatting on the serialize path.
/// <c>ElementJsonConverter.ToJsonValue</c> formatted <c>DateTime</c>/<c>DateTimeOffset</c> with a custom
/// format string and no <c>IFormatProvider</c>, so the calendar came from <c>CurrentCulture</c>: the
/// "yyyy" component rendered the Buddhist year on th-TH, the UmAlQura year on ar-SA and the Persian
/// year on fa-IR. That is a wrong date written into the FHIR payload, not merely odd formatting.
///
/// Culture is mutated synchronously and restored in a <c>finally</c>, matching the convention already
/// used elsewhere in the repo — see the note on SchemaAwareElementCultureInvarianceTests.
/// </summary>
public class ElementJsonConverterCultureInvarianceTests
{
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void GivenADateTime_WhenTheHostCultureVaries_ThenTheEmittedJsonDoesNot(string culture)
    {
        // Arrange
        var value = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var json = UnderCulture(culture, () => ElementJsonConverter.ToJsonValue(value).GetValue<string>());

        // Assert - a Buddhist-calendar render would read "2567-01-15".
        Assert.Equal("2024-01-15T10:30:00Z", json);
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void GivenADateTimeOffset_WhenTheHostCultureVaries_ThenTheEmittedJsonDoesNot(string culture)
    {
        // Arrange
        var value = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var json = UnderCulture(culture, () => ElementJsonConverter.ToJsonValue(value).GetValue<string>());

        // Assert
        Assert.Equal("2024-01-15T10:30:00+00:00", json);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void GivenADecimal_WhenConvertedToStringUnderAVaryingHostCulture_ThenTheResultDoesNotVary(string culture)
    {
        // Arrange - de-DE renders "1234,5" and ar-SA renders "1234٫5" (U+066B) without an explicit provider.
        var value = 1234.5m;

        // Act
        var text = UnderCulture(culture, () => PrimitiveTypeConverter.ConvertTo<string>(value));

        // Assert
        Assert.Equal("1234.5", text);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    public void GivenANegativeDecimal_WhenConvertedToStringUnderAVaryingHostCulture_ThenTheResultDoesNotVary(string culture)
    {
        // Arrange - ar-SA's NegativeSign is not ascii '-'.
        var value = -0.125m;

        // Act
        var text = UnderCulture(culture, () => PrimitiveTypeConverter.ConvertTo<string>(value));

        // Assert
        Assert.Equal("-0.125", text);
    }

    private static T UnderCulture<T>(string culture, Func<T> act)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
