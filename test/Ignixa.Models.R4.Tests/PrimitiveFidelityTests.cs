// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.R4.Tests;

/// <summary>
/// CHARACTERIZATION of FHIR primitive round-trip fidelity in the JSON-primary runtime.
///
/// FHIR mandates that the precision of a <c>decimal</c> (significant digits, trailing zeros,
/// exponent form) survives a parse -> serialize round-trip. The generated typed accessors expose
/// <c>decimal?</c> and write via <c>JsonValue.Create(decimal)</c>; these tests pin down exactly
/// where that preserves precision and where it does not. They are the empirical backing for
/// docs/features/typed-models/investigations/primitive-fidelity.md and the regression cover for
/// the decimal escape-hatch (<see cref="Ignixa.Models.Quantity.ValueRaw"/>).
///
/// Runtime under test:
///   - parse:     <see cref="ResourceJsonNode.Parse(string)"/> -> custom converter does
///                <c>JsonNode.Parse(ref reader)</c>, so number leaves are <c>JsonValue</c>
///                instances backed by a <c>JsonElement</c> (the RAW token text is retained).
///   - typed get: <c>GetProperty&lt;decimal?&gt;("value")</c> -> <c>JsonValue.GetValue&lt;decimal?&gt;()</c>.
///   - typed set: <c>SetProperty("value", value)</c> -> <c>JsonValue.Create(decimal)</c>
///                (leaf is now backed by a CLR <c>decimal</c>, not the raw token).
///   - serialize: <c>MutableNode.ToJsonString()</c>.
///
/// Each test's comment records the OBSERVED behavior; assertions pin the actual (possibly lossy)
/// result so this file documents reality, not aspiration.
///
/// Ported from the typed-models spike (PrimitiveFidelitySpikeTests) against the graduated generated
/// <c>Ignixa.Models.Quantity</c> (same <c>decimal? Value</c> plus the new <c>ValueRaw</c>).
/// </summary>
public sealed class PrimitiveFidelityTests
{
    private static string QuantityJson(string rawValueToken) =>
        $$"""{ "value": {{rawValueToken}}, "unit": "mg", "code": "mg" }""";

    private static string ObservationJson(string rawValueToken) =>
        $$"""
        {
          "resourceType": "Observation",
          "status": "final",
          "valueQuantity": { "value": {{rawValueToken}}, "unit": "mg", "code": "mg" }
        }
        """;

    private static Ignixa.Models.Quantity ParseQuantity(string rawValueToken) =>
        new Ignixa.Models.Quantity((JsonObject)JsonNode.Parse(QuantityJson(rawValueToken))!);

    // =============================================================================================
    // 1. READ-ONLY round-trip (NO mutation) — does STJ JsonNode preserve the raw number token?
    // =============================================================================================

    [Theory]
    [InlineData("185.0")]
    [InlineData("1.230")]
    [InlineData("1.00000000000000000000000001")] // 27 sig digits — beyond decimal's 28-29 but parseable as a number token
    [InlineData("1.00e2")]                          // exponent form
    [InlineData("100")]
    public void GivenUntouchedDecimal_WhenSerialized_ThenRawTokenIsPreservedByteForByte(string rawToken)
    {
        // OBSERVED: JsonNode.Parse retains the original number token verbatim for untouched leaves.
        // ToJsonString writes the raw token back unchanged — trailing zeros, high precision and
        // exponent form all survive because nothing ever materialized the value as a CLR number.
        var node = (JsonObject)JsonNode.Parse(QuantityJson(rawToken))!;

        var serialized = node.ToJsonString();

        serialized.ShouldContain($"\"value\":{rawToken}");
    }

    [Fact]
    public void GivenUntouchedObservationDecimal_WhenResourceSerialized_ThenDecimalPreserved()
    {
        // OBSERVED: same fidelity through the real Parse path (custom converter -> JsonNode.Parse).
        var observation = ResourceJsonNode.Parse(ObservationJson("185.0"));

        var serialized = observation.MutableNode.ToJsonString();

        serialized.ShouldContain("\"value\":185.0");
    }

    // =============================================================================================
    // 2. TYPED READ fidelity — does CLR decimal preserve scale, and what about out-of-range?
    // =============================================================================================

    [Theory]
    [InlineData("185.0", "185.0")]
    [InlineData("1.230", "1.230")]
    [InlineData("100", "100")]
    public void GivenDecimalToken_WhenReadAsClrDecimal_ThenScaleIsPreserved(string rawToken, string expectedToString)
    {
        // OBSERVED: System.Decimal carries its own scale, and JsonValue.GetValue<decimal>() parses
        // the token preserving trailing zeros. "185.0" -> decimal whose ToString() == "185.0".
        var quantity = ParseQuantity(rawToken);

        decimal? value = quantity.Value;

        value.ShouldNotBeNull();
        value.Value.ToString(CultureInfo.InvariantCulture).ShouldBe(expectedToString);
    }

    [Fact]
    public void GivenModeratelyHighPrecision_WhenReadAsClrDecimal_ThenItRoundTripsExactly()
    {
        // OBSERVED (surprise): "1.00000000000000000000000001" is 27 significant digits, which FITS
        // inside System.Decimal's 28-29 significant-digit capacity. It round-trips EXACTLY -- no
        // rounding. So "high precision" alone is not the loss boundary; the boundary is decimal's
        // 28-29 sig-digit / ~7.9e28 magnitude limit (see the two tests below).
        var quantity = ParseQuantity("1.00000000000000000000000001");

        decimal? value = quantity.Value;

        value.ShouldNotBeNull();
        value.Value.ToString(CultureInfo.InvariantCulture).ShouldBe("1.00000000000000000000000001");
    }

    [Fact]
    public void GivenPrecisionExceedingDecimalCapacity_WhenReadAsClrDecimal_ThenSignificandIsRoundedAndLost()
    {
        // OBSERVED: a token with MORE significant digits than System.Decimal can hold (here 35
        // fractional digits) is silently ROUNDED to decimal's capacity. GetValue<decimal> does not
        // throw -- the trailing precision is dropped. The raw token and the materialized decimal
        // now disagree, so the decimal? accessor is lossy for FHIR decimals beyond ~28-29 digits.
        const string rawToken = "0.12345678901234567890123456789012345"; // 35 sig digits
        var quantity = ParseQuantity(rawToken);

        decimal? value = quantity.Value;

        value.ShouldNotBeNull();
        value.Value.ToString(CultureInfo.InvariantCulture).ShouldNotBe(rawToken);
        value.Value.ToString(CultureInfo.InvariantCulture).Length.ShouldBeLessThan(rawToken.Length);
    }

    [Fact]
    public void GivenMagnitudeBeyondDecimalRange_WhenReadAsClrDecimal_ThenThrows()
    {
        // OBSERVED: a magnitude beyond System.Decimal's range (~7.9e28) cannot be represented at all;
        // GetValue<decimal> THROWS (rather than rounding). A FHIR decimal is unbounded in magnitude,
        // so decimal? is not merely lossy but unusable for very large values -- it errors the read.
        // The specific type is InvalidOperationException: JsonValue.GetValue<decimal?>() rejects a
        // number outside decimal's range (verified actual type; not OverflowException/FormatException).
        var quantity = ParseQuantity("1e40");

        Should.Throw<InvalidOperationException>(() => _ = quantity.Value);
    }

    // =============================================================================================
    // 3. SET round-trip — does writing a decimal via JsonValue.Create preserve scale?
    // =============================================================================================

    [Theory]
    [InlineData(185.0)]
    [InlineData(1.230)]
    public void GivenDecimalSetViaTypedAccessor_WhenSerialized_ThenScaleIsPreserved(double seed)
    {
        // OBSERVED: a decimal literal keeps its scale (185.0m has scale 1; 1.230m has scale 3).
        // JsonValue.Create(decimal) preserves that scale on write, so the trailing zeros survive
        // the SET path -- PROVIDED the value can be expressed as a decimal literal in the first place.
        var quantity = new Ignixa.Models.Quantity(new JsonObject());
        decimal scaled = decimal.Parse(seed.ToString("0.000", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        quantity.Value = scaled;

        var serialized = quantity.MutableNode.ToJsonString();
        serialized.ShouldContain($"\"value\":{scaled.ToString(CultureInfo.InvariantCulture)}");
    }

    [Fact]
    public void GivenDecimalLiteralWithExplicitScale_WhenSetAndSerialized_ThenTrailingZerosSurvive()
    {
        // OBSERVED: decimal preserves scale as part of its representation. 185.0m -> "185.0".
        var quantity = new Ignixa.Models.Quantity(new JsonObject());

        quantity.Value = 185.0m;

        quantity.MutableNode.ToJsonString().ShouldContain("\"value\":185.0");
    }

    [Fact]
    public void GivenPrecisionBeyondDecimal_WhenForcedThroughDecimalSet_ThenPrecisionCannotBeRepresented()
    {
        // OBSERVED: there is NO way to push a >28-29-digit FHIR decimal through the decimal? accessor
        // without first losing precision -- decimal.Parse already rounds it before any serialization.
        // The set path is bounded by System.Decimal's capacity, independent of how it is written out.
        const string rawText = "0.12345678901234567890123456789012345"; // 35 sig digits
        decimal rounded = decimal.Parse(rawText, CultureInfo.InvariantCulture);

        rounded.ToString(CultureInfo.InvariantCulture).ShouldNotBe(rawText);
    }

    [Fact]
    public void GivenRawDecimalPreservingSet_WhenSerialized_ThenAnyPrecisionSurvives()
    {
        // OBSERVED: the ESCAPE HATCH. Writing the raw JsonNode (parsed token) instead of a CLR
        // decimal preserves arbitrary precision. SetProperty(string, JsonNode) bypasses
        // JsonValue.Create(decimal) entirely. This is what a raw-preserving accessor would do --
        // even a 35-digit value the decimal? accessor cannot represent survives byte-for-byte.
        const string rawText = "0.12345678901234567890123456789012345";
        var quantity = new Ignixa.Models.Quantity(new JsonObject());

        quantity.SetProperty("value", JsonNode.Parse(rawText));

        quantity.MutableNode.ToJsonString().ShouldContain($"\"value\":{rawText}");
    }

    [Fact]
    public void GivenValueRawSetterWithHighPrecisionToken_WhenSerializedAndReparsed_ThenTokenSurvivesByteForByte()
    {
        // Exercises the generated ValueRaw SETTER (the prior tests only read ValueRaw). Assigning a
        // raw JsonNode bypasses JsonValue.Create(decimal), so a 35-significant-digit token survives a
        // serialize -> reparse round-trip byte-for-byte -- proving the setter does not coerce to decimal.
        const string rawText = "0.12345678901234567890123456789012345"; // 35 sig digits
        var quantity = new Ignixa.Models.Quantity(new JsonObject());

        quantity.ValueRaw = JsonNode.Parse(rawText);

        var serialized = quantity.MutableNode.ToJsonString();
        serialized.ShouldContain($"\"value\":{rawText}");

        var reparsed = new Ignixa.Models.Quantity((JsonObject)JsonNode.Parse(serialized)!);
        reparsed.ValueRaw!.ToJsonString().ShouldBe(rawText);
    }

    [Fact]
    public void GivenValueRawSetThenReadAsDecimal_ThenDecimalProjectionRoundsPerDocumentedLimit()
    {
        // After ValueRaw stores a token beyond decimal's ~28-29 digit capacity, reading the typed
        // Value (decimal?) rounds to that limit -- the documented lossy projection. The rounded text
        // is strictly shorter than the raw token and differs from it (the final retained digit may
        // round up, so it is NOT necessarily a prefix). ValueRaw still holds the full token verbatim.
        const string rawText = "0.12345678901234567890123456789012345"; // 35 sig digits
        var quantity = new Ignixa.Models.Quantity(new JsonObject());

        quantity.ValueRaw = JsonNode.Parse(rawText);

        decimal? value = quantity.Value;
        value.ShouldNotBeNull();
        var rounded = value.Value.ToString(CultureInfo.InvariantCulture);
        rounded.Length.ShouldBeLessThan(rawText.Length);
        rounded.ShouldNotBe(rawText);

        // The raw escape-hatch is untouched by the lossy decimal read.
        quantity.ValueRaw!.ToJsonString().ShouldBe(rawText);
    }

    // =============================================================================================
    // 4. WHOLE-VALUE mutation of a SIBLING field — does an untouched decimal stay byte-identical?
    //    (The realistic case: editing one field must not reformat the others.)
    // =============================================================================================

    [Fact]
    public void GivenSiblingFieldMutated_WhenSerialized_ThenUntouchedDecimalStaysByteIdentical()
    {
        // OBSERVED: mutating a DIFFERENT field (unit) leaves the decimal leaf as its original
        // JsonElement-backed JsonValue. The raw "185.0" token is untouched and serializes verbatim.
        // This is the common edit pattern and it is lossless for the fields you do not touch.
        var quantity = ParseQuantity("185.0");

        quantity.Unit = "milligram";

        var serialized = quantity.MutableNode.ToJsonString();
        serialized.ShouldContain("\"value\":185.0");
        serialized.ShouldContain("\"unit\":\"milligram\"");
    }

    [Fact]
    public void GivenSiblingStringMutatedOnResource_WhenSerialized_ThenNestedDecimalUnchanged()
    {
        // OBSERVED: same guarantee at resource scope. Touching Observation.status does not reformat
        // the nested valueQuantity.value "1.230" token.
        var observation = ResourceJsonNode.Parse(ObservationJson("1.230"));

        observation.MutableNode["status"] = "amended";

        observation.MutableNode.ToJsonString().ShouldContain("\"value\":1.230");
    }

    [Fact]
    public void GivenDecimalReadThenSiblingMutated_WhenSerialized_ThenReadingDidNotDirtyTheToken()
    {
        // OBSERVED: a typed READ of the decimal (GetValue<decimal>) does NOT replace the underlying
        // leaf -- the JsonValue is still the original token. Reading is non-destructive; only a SET
        // swaps the leaf for a CLR-backed one. So "read decimal, edit a sibling, serialize" is lossless.
        var quantity = ParseQuantity("1.00000000000000000000000001");

        _ = quantity.Value; // materialize once (and discard) -- does this mutate the node?
        quantity.Unit = "milligram";

        // If reading were destructive the high-precision token would be gone. It is not.
        quantity.MutableNode.ToJsonString().ShouldContain("\"value\":1.00000000000000000000000001");
    }

    // =============================================================================================
    // 5. DATE / DATETIME / INSTANT as string — confirm string treatment is trivially lossless.
    // =============================================================================================

    [Theory]
    [InlineData("1974-12-25")]   // full date
    [InlineData("1974")]          // partial: year only
    [InlineData("1974-12")]       // partial: year-month
    [InlineData("1974-12-25T14:35:45.123-05:00")] // instant w/ fractional seconds + tz offset
    public void GivenDateOrInstantAsString_WhenReadSetAndSerialized_ThenValueIsLossless(string raw)
    {
        // OBSERVED: dates/dateTimes/instants are surfaced as string. A JSON string is stored and
        // emitted verbatim by STJ -- no parsing to DateTimeOffset, no normalization, no precision
        // loss. Partial dates and timezone offsets survive exactly. String treatment is correct
        // and trivially lossless for these types.
        var patient = ResourceJsonNode.Parse(
            $$"""{ "resourceType": "Patient", "birthDate": {{System.Text.Json.JsonSerializer.Serialize(raw)}} }""")
            .As<Ignixa.Models.R4.Patient>();

        // read
        patient.BirthDate.ShouldBe(raw);

        // set (round-trips identically)
        patient.BirthDate = raw;

        // serialize
        patient.MutableNode.ToJsonString().ShouldContain(System.Text.Json.JsonSerializer.Serialize(raw));
        ResourceJsonNode.Parse(patient.MutableNode.ToJsonString()).As<Ignixa.Models.R4.Patient>().BirthDate.ShouldBe(raw);
    }
}
