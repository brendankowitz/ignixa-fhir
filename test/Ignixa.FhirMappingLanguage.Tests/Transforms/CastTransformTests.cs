/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Tests.Conformance;
using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Transforms;

public class CastTransformTests
{
    public static TheoryData<string> StringTypes => new()
    {
        "string", "uri", "url", "canonical", "code", "oid", "id", "uuid", "markdown", "base64Binary"
    };

    [Theory]
    [MemberData(nameof(StringTypes))]
    public void GivenAStringFamilyTarget_WhenCasting_ThenUnqualifiedAndFhirNamesConvertToString(string typeName)
    {
        Cast(123, typeName).ShouldBeOfType<string>().ShouldBe("123");
        Cast(false, $"FHIR.{typeName}").ShouldBeOfType<string>().ShouldBe("false");
        Cast("unchanged", $"fhir.{typeName.ToUpperInvariant()}").ShouldBe("unchanged");
    }

    [Theory]
    [InlineData("string", "System.String", "string", "value")]
    [InlineData("boolean", "System.Boolean", "boolean", true)]
    [InlineData("integer", "System.Integer", "integer", 42)]
    public void GivenSystemNames_WhenCasting_ThenAliasesReturnTheSameScalar(
        string sourceType, string systemType, string fhirType, object value)
    {
        var source = CreateElement(sourceType, value);

        Cast(source, systemType).ShouldBe(value);
        Cast(source, $"FHIR.{fhirType}").ShouldBe(value);
    }

    [Theory]
    [InlineData("integer64")]
    [InlineData("FHIR.integer64")]
    [InlineData("System.Long")]
    [InlineData("long")]
    public void GivenLongTargets_WhenCasting_ThenPreservesTheFullSigned64BitRange(string typeName)
    {
        Cast("9223372036854775807", typeName).ShouldBeOfType<long>().ShouldBe(long.MaxValue);
        Cast("-9223372036854775808", typeName).ShouldBeOfType<long>().ShouldBe(long.MinValue);
        Should.Throw<OverflowException>(() => Cast("9223372036854775808", typeName));
    }

    [Theory]
    [InlineData("unsignedInt", 0)]
    [InlineData("FHIR.unsignedInt", 0)]
    [InlineData("positiveInt", 1)]
    [InlineData("FHIR.positiveInt", 1)]
    public void GivenConstrainedIntegers_WhenCasting_ThenEnforcesBothBounds(string typeName, int minimum)
    {
        Cast(minimum.ToString(CultureInfo.InvariantCulture), typeName).ShouldBeOfType<int>().ShouldBe(minimum);
        Cast("2147483647", typeName).ShouldBeOfType<int>().ShouldBe(int.MaxValue);
        Should.Throw<OverflowException>(() => Cast(minimum - 1, typeName));
        Should.Throw<OverflowException>(() => Cast("2147483648", typeName));
        Should.Throw<FormatException>(() => Cast(1.5m, typeName));
    }

    [Theory]
    [InlineData("integer")]
    [InlineData("integer64")]
    public void GivenFractionalNumbers_WhenCastingToInteger_ThenRejectsInsteadOfRounding(string typeName)
    {
        Should.Throw<FormatException>(() => Cast(2.5m, typeName));
        Cast(2.0m, typeName).ShouldBe(typeName == "integer" ? (object)2 : 2L);
    }

    [Fact]
    public void GivenNonEnglishCulture_WhenCastingAliases_ThenUsesInvariantFormats()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            Cast("12.50", "System.Decimal").ShouldBeOfType<decimal>().ShouldBe(12.50m);
            Cast(12.50m, "FHIR.string").ShouldBe("12.50");
            Cast("2024-02", "FHIR.date").ShouldBeOfType<FhirTemporal>().Literal.ShouldBe("2024-02");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void GivenBinaryBytes_WhenCastingToBase64_ThenEncodesTheBytes()
    {
        Cast(new byte[] { 0, 1, 254, 255 }, "FHIR.base64Binary").ShouldBe("AAH+/w==");
        Cast("AAH+/w==", "base64Binary").ShouldBe("AAH+/w==");
    }

    [Theory]
    [InlineData("2024", "date", FhirPrimitive.Date, FhirTemporalPrecision.Year)]
    [InlineData("2024-02", "FHIR.date", FhirPrimitive.Date, FhirTemporalPrecision.Month)]
    [InlineData("2024-02-29", "System.Date", FhirPrimitive.Date, FhirTemporalPrecision.Day)]
    [InlineData("2024-02", "System.DateTime", FhirPrimitive.DateTime, FhirTemporalPrecision.Month)]
    [InlineData("2024-02-29T23:45:01.123-05:00", "FHIR.dateTime", FhirPrimitive.DateTime, FhirTemporalPrecision.Millisecond)]
    [InlineData("2024-02-29T23:45:01Z", "instant", FhirPrimitive.Instant, FhirTemporalPrecision.Second)]
    [InlineData("23:45:01.123", "FHIR.time", FhirPrimitive.Time, FhirTemporalPrecision.Millisecond)]
    [InlineData("23:45:01", "System.Time", FhirPrimitive.Time, FhirTemporalPrecision.Second)]
    public void GivenTemporalTargets_WhenCasting_ThenPreservesLiteralPrecisionAndKind(
        string literal, string typeName, FhirPrimitive kind, FhirTemporalPrecision precision)
    {
        var result = Cast(literal, typeName).ShouldBeOfType<FhirTemporal>();

        result.Literal.ShouldBe(literal);
        result.Kind.ShouldBe(kind);
        result.Precision.ShouldBe(precision);
    }

    [Fact]
    public void GivenTypedDateTime_WhenCastingToDate_ThenExtractsTheLocalDateWithoutTimezoneConversion()
    {
        FhirTemporal.TryParse("2024-02-29T23:45:01-05:00", FhirPrimitive.DateTime, out var input).ShouldBeTrue();

        var result = Cast(input!, "date").ShouldBeOfType<FhirTemporal>();

        result.Kind.ShouldBe(FhirPrimitive.Date);
        result.Literal.ShouldBe("2024-02-29");
        Cast(CreateElement("instant", input!.Literal), "FHIR.date").ShouldBeOfType<FhirTemporal>()
            .Literal.ShouldBe("2024-02-29");
    }

    [Fact]
    public void GivenDateTimeOffset_WhenCasting_ThenPreservesItsOffsetAndClockComponents()
    {
        var input = new DateTimeOffset(2024, 2, 29, 23, 45, 1, TimeSpan.FromHours(-5));

        Cast(input, "dateTime").ShouldBeOfType<FhirTemporal>().Literal.ShouldBe("2024-02-29T23:45:01-05:00");
        Cast(input, "date").ShouldBeOfType<FhirTemporal>().Literal.ShouldBe("2024-02-29");
        Cast(input, "time").ShouldBeOfType<FhirTemporal>().Literal.ShouldBe("23:45:01");
    }

    [Theory]
    [InlineData("2023-02-29", "date")]
    [InlineData("0000", "date")]
    [InlineData("2024-13", "dateTime")]
    [InlineData("2024-02-29T23:45:01Z", "date")]
    [InlineData("2024-02-29T23:45:01", "FHIR.dateTime")]
    [InlineData("2024", "instant")]
    [InlineData("2024-02-29T23:45:01", "instant")]
    [InlineData("25:00:00", "time")]
    [InlineData("12:00:00Z", "FHIR.time")]
    [InlineData("not-a-date", "dateTime")]
    public void GivenInvalidTemporalValues_WhenCasting_ThenFailsExplicitly(string value, string typeName)
    {
        Should.Throw<FormatException>(() => Cast(value, typeName));
    }

    [Theory]
    [InlineData("a", "year")]
    [InlineData("mo", "month")]
    [InlineData("d", "day")]
    [InlineData("h", "hour")]
    [InlineData("min", "minute")]
    [InlineData("s", "second")]
    [InlineData("mg", "mg")]
    public void GivenAUcumFhirQuantity_WhenCastingToSystemQuantity_ThenUsesTheFhirPathUnit(string code, string unit)
    {
        var source = CreateElement("Quantity", new JsonObject
        {
            ["value"] = 2.50m,
            ["unit"] = "display",
            ["system"] = "http://unitsofmeasure.org",
            ["code"] = code
        });

        var result = Cast(source, "System.Quantity").ShouldBeOfType<FhirQuantity>();

        result.Value.ShouldBe(2.50m);
        result.Unit.ShouldBe(unit);
        Cast(source, "FHIR.Quantity").ShouldBeSameAs(source);
    }

    [Theory]
    [InlineData("year", "a")]
    [InlineData("days", "d")]
    [InlineData("mg", "mg")]
    public void GivenASystemQuantity_WhenCastingToFhirQuantity_ThenWritesUcumFields(string unit, string code)
    {
        var source = new FhirQuantity(2.50m, unit);

        var result = Cast(source, "FHIR.Quantity").ShouldBeOfType<JsonObject>();

        result["value"]!.GetValue<decimal>().ShouldBe(2.50m);
        result["unit"]!.GetValue<string>().ShouldBe(unit);
        result["system"]!.GetValue<string>().ShouldBe("http://unitsofmeasure.org");
        result["code"]!.GetValue<string>().ShouldBe(code);
        Cast(source, "System.Quantity").ShouldBeSameAs(source);
    }

    [Theory]
    [InlineData("""{"value":1,"unit":"mg"}""")]
    [InlineData("""{"value":1,"system":"http://example.org/units","code":"mg"}""")]
    [InlineData("""{"value":1,"system":"http://unitsofmeasure.org"}""")]
    [InlineData("""{"system":"http://unitsofmeasure.org","code":"mg"}""")]
    [InlineData("""{"value":1,"comparator":"<","system":"http://unitsofmeasure.org","code":"mg"}""")]
    public void GivenANonConvertibleQuantity_WhenCastingToSystemQuantity_ThenRejectsWithoutInventingUnits(string json)
    {
        var source = CreateElement("Quantity", JsonNode.Parse(json)!);

        Should.Throw<InvalidOperationException>(() => Cast(source, "System.Quantity"));
        Cast(source, "Quantity").ShouldBeSameAs(source);
    }

    [Fact]
    public void GivenAComplexValue_WhenCasting_ThenOnlyTheMatchingTypePassesThrough()
    {
        var name = CreateElement("HumanName", new JsonObject { ["family"] = "Alpha" });

        Cast(name, "FHIR.HumanName").ShouldBeSameAs(name);
        Should.Throw<NotSupportedException>(() => Cast(name, "Patient"));
        Should.Throw<NotSupportedException>(() => Cast("value", "System.uri"));
        Should.Throw<NotSupportedException>(() => Cast("value", "notAType"));
    }

    [Fact]
    public void GivenMissingValuesOrInvalidArity_WhenCasting_ThenDoesNotProduceDefaultScalars()
    {
        var transform = StandardTransforms.Get("cast")!;

        Should.Throw<ArgumentNullException>(() => Cast(null!, "integer"));
        Should.Throw<ArgumentException>(() => Cast(CreateElement("string"), "boolean"));
        Should.Throw<ArgumentException>(() => transform.Execute(["value"], new MappingContext()));
        Should.Throw<ArgumentException>(() => transform.Execute(["value", "string", "extra"], new MappingContext()));
    }

    [Theory]
    [InlineData("123", "FHIR.code", """{"valueCode":"123"}""")]
    [InlineData("https://example.org", "uri", """{"valueUri":"https://example.org"}""")]
    [InlineData("true", "System.Boolean", """{"valueBoolean":true}""")]
    [InlineData("1", "positiveInt", """{"valuePositiveInt":1}""")]
    [InlineData("0", "FHIR.unsignedInt", """{"valueUnsignedInt":0}""")]
    [InlineData("9223372036854775807", "System.Long", """{"valueInteger64":"9223372036854775807"}""")]
    [InlineData("12.50", "System.Decimal", """{"valueDecimal":12.50}""")]
    [InlineData("2024-02", "FHIR.date", """{"valueDate":"2024-02"}""")]
    [InlineData("2024-02", "System.DateTime", """{"valueDateTime":"2024-02"}""")]
    [InlineData("2024-02-29T23:45:01-05:00", "FHIR.instant", """{"valueInstant":"2024-02-29T23:45:01-05:00"}""")]
    [InlineData("12:34:56", "System.Time", """{"valueTime":"12:34:56"}""")]
    public void GivenCastInAMapping_WhenWritingAChoice_ThenPreservesRequestedTypeAndWireValue(
        string input, string targetType, string expectedValue)
    {
        var source = new JsonObject { ["resourceType"] = "Observation", ["valueString"] = input };
        var actual = Execute(source.ToJsonString(), $"cast(v, '{targetType}')");

        AssertTarget(actual, expectedValue);
    }

    [Fact]
    public void GivenQuantityCastInAMapping_WhenWritingAChoice_ThenProducesFhirQuantityNotClrProperties()
    {
        var actual = Execute("""{"resourceType":"Observation","valueString":"unused"}""", "cast(2.50 'mg', 'Quantity')");

        AssertTarget(actual, """{"valueQuantity":{"value":2.50,"unit":"mg","system":"http://unitsofmeasure.org","code":"mg"}}""");
    }

    [Fact]
    public void GivenTypedQuantityCastInAMapping_WhenCopying_ThenPreservesItsCodeAndMetadata()
    {
        var actual = Execute("""
            {"resourceType":"Observation","valueQuantity":{
              "id":"source-quantity","value":1.25,"unit":"tablets","system":"http://example.org/units","code":"tab"}}
            """, "cast(v, 'FHIR.Quantity')");

        AssertTarget(actual, """
            {"valueQuantity":{"id":"source-quantity","value":1.25,"unit":"tablets","system":"http://example.org/units","code":"tab"}}
            """);
    }

    [Fact]
    public void GivenInteger64JsonInput_WhenCreatingAndCasting_ThenPreservesPrecisionAndQuotedWireValue()
    {
        var element = CreateElement("integer64", JsonValue.Create("9223372036854775807"));

        Cast(element, "System.Long").ShouldBeOfType<long>().ShouldBe(long.MaxValue);
        var actual = Execute("""{"resourceType":"Observation","valueString":"unused"}""",
            "cast(9223372036854775807L, 'FHIR.integer64')");
        AssertTarget(actual, """{"valueInteger64":"9223372036854775807"}""");
    }

    [Fact]
    public void GivenAnInvalidCastInAMapping_WhenExecutingStrictly_ThenTheConversionErrorSurfaces()
    {
        Should.Throw<OverflowException>(() =>
            Execute("""{"resourceType":"Observation","valueString":"-1"}""", "cast(v, 'unsignedInt')"));
    }

    [Fact]
    public void GivenAFhirPathTemporalLiteral_WhenCastingToDate_ThenRetainsItsTypeBeforeConversion()
    {
        var actual = Execute("""{"resourceType":"Observation","valueString":"unused"}""",
            "cast(@2024-02-29T23:45:01-05:00, 'date')");

        AssertTarget(actual, """{"valueDate":"2024-02-29"}""");
    }

    private static object Cast(object value, string typeName) =>
        StandardTransforms.Get("cast")!.Execute([value, typeName], new MappingContext());

    private static IElement CreateElement(string typeName, object? value = null) =>
        new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => FhirVersion.R5.GetSchemaProvider())
            .CreateElement(typeName, value);

    private static ResourceJsonNode Execute(string input, string transform)
    {
        var schema = FhirVersion.R5.GetSchemaProvider();
        var source = ResourceJsonNode.Parse(input);
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var context = new MappingContext();
        context.SetSource("src", source.ToElement(schema));
        context.SetTarget("tgt", target.ToElement(schema));
        context.SetTargetResource("tgt", target);
        var mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema);
        var map = new MappingParser().Parse($$"""
            map 'http://example.org/cast' = 'Cast'
            group Main(source src : Observation, target tgt : Patient) {
              src.value as v -> tgt.extension as e then {
                v -> e.value = {{transform}};
              };
            }
            """);

        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator).Execute(map, context);

        context.Errors.ShouldBeEmpty();
        return target;
    }

    private static void AssertTarget(ResourceJsonNode actual, string expectedValue) =>
        CanonicalJson.Canonicalize(actual.MutableNode().ToJsonString()).ShouldBe(
            CanonicalJson.Canonicalize($$"""{"resourceType":"Patient","extension":[{{expectedValue}}]}"""));
}
