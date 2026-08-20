// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Xunit;
using IgnixaQuantity = Ignixa.Abstractions.FhirQuantity;
using P = Hl7.Fhir.ElementModel.Types;

namespace Ignixa.Extensions.Tests.FirelySdk;

/// <summary>
/// Tests that each adapter presents the primitive value contract its own SDK expects, rather than
/// passing the other SDK's representation straight through.
/// </summary>
public class FirelyPrimitiveValueContractTests
{
    #region Ignixa -> Firely

    [Theory]
    [InlineData("dateTime", "2013-01-01T11:22:33+10:00")]
    [InlineData("dateTime", "2013-01-01T11:22:33")]
    [InlineData("dateTime", "2013-01-01T11:22")]
    [InlineData("dateTime", "2013")]
    [InlineData("instant", "2013-01-01T11:22:33.123Z")]
    public void GivenIgnixaDateTimeString_WhenReadThroughTypedElementAdapter_ThenReturnsFirelyDateTime(string instanceType, string text)
    {
        // Arrange
        var element = new StubElement { InstanceType = instanceType, Value = text };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var dateTime = Assert.IsType<P.DateTime>(value);
        Assert.Equal(text, dateTime.ToString());
    }

    [Fact]
    public void GivenIgnixaDateString_WhenReadThroughTypedElementAdapter_ThenReturnsFirelyDate()
    {
        // Arrange
        var element = new StubElement { InstanceType = "date", Value = "2013-01-01" };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var date = Assert.IsType<P.Date>(value);
        Assert.Equal("2013-01-01", date.ToString());
    }

    [Fact]
    public void GivenIgnixaTimeString_WhenReadThroughTypedElementAdapter_ThenReturnsFirelyTime()
    {
        // Arrange
        var element = new StubElement { InstanceType = "time", Value = "11:22:33" };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var time = Assert.IsType<P.Time>(value);
        Assert.Equal("11:22:33", time.ToString());
    }

    [Fact]
    public void GivenIgnixaDateTimeOffset_WhenReadThroughTypedElementAdapter_ThenReturnsFirelyDateTime()
    {
        // IElement.Value permits DateTimeOffset for the temporal types as well as a string.

        // Arrange
        var instant = new DateTimeOffset(2013, 1, 1, 11, 22, 33, TimeSpan.Zero);
        var element = new StubElement { InstanceType = "instant", Value = instant };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var dateTime = Assert.IsType<P.DateTime>(value);
        Assert.Equal(instant, dateTime.ToDateTimeOffset(TimeSpan.Zero));
    }

    [Fact]
    public void GivenIgnixaInteger64String_WhenReadThroughTypedElementAdapter_ThenReturnsLong()
    {
        // Arrange
        var element = new StubElement { InstanceType = "integer64", Value = "9007199254740993" };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.Equal(9007199254740993L, Assert.IsType<long>(value));
    }

    [Theory]
    [InlineData("string", "hello")]
    [InlineData("code", "official")]
    [InlineData("uri", "http://example.org")]
    [InlineData("base64Binary", "SGVsbG8=")]
    public void GivenIgnixaStringBackedPrimitive_WhenReadThroughTypedElementAdapter_ThenPassesThroughUnchanged(string instanceType, string text)
    {
        // Arrange
        var element = new StubElement { InstanceType = instanceType, Value = text };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert - reference equality, so a translation that rebuilt an equal string would fail.
        Assert.Same(text, value);
    }

    [Fact]
    public void GivenIgnixaNonStringPrimitives_WhenReadThroughTypedElementAdapter_ThenPassesThroughUnchanged()
    {
        // The two SDKs already agree on these, so they must not be disturbed.

        // Arrange & Act & Assert
        Assert.Equal(true, new TypedElementAdapter(new StubElement { InstanceType = "boolean", Value = true }).Value);
        Assert.Equal(42, new TypedElementAdapter(new StubElement { InstanceType = "integer", Value = 42 }).Value);
        Assert.Equal(1.5m, new TypedElementAdapter(new StubElement { InstanceType = "decimal", Value = 1.5m }).Value);
    }

    [Theory]
    [InlineData("dateTime", "not-a-date")]
    [InlineData("date", "2013-13-45")]
    [InlineData("time", "99:99")]
    [InlineData("integer64", "9223372036854775808")]
    [InlineData("integer64", "12.0")]
    [InlineData("integer64", " 12 ")]
    public void GivenUnparseableIgnixaPrimitive_WhenReadThroughTypedElementAdapter_ThenReturnsRawTextWithoutThrowing(string instanceType, string text)
    {
        // Degrading to the raw text keeps navigation over a malformed resource possible instead of
        // throwing mid-traversal. It is NOT a repair: the raw string reaches Firely exactly as it
        // did before this translation existed, so downstream behaviour on bad data is unchanged.
        // Concretely, that behaviour is deferred and consumer-dependent - navigation, toString()
        // and serialization see a plain string, while a consumer that coerces it to a temporal
        // throws, reported as a type mismatch far from the element that is actually malformed.
        // Note " 12 " is rejected deliberately - FHIR's integer64 regex forbids surrounding
        // whitespace, which long.TryParse would otherwise accept under NumberStyles.Integer.

        // Arrange
        var element = new StubElement { InstanceType = instanceType, Value = text };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.Equal(text, value);
    }

    [Theory]
    [InlineData("DateTime", "2013-01-01T11:22:33+10:00", typeof(P.DateTime))]
    [InlineData("DATE", "2013-01-01", typeof(P.Date))]
    [InlineData("Time", "11:22:33", typeof(P.Time))]
    [InlineData("Integer64", "42", typeof(long))]
    public void GivenInstanceTypeInUnexpectedCasing_WhenReadThroughTypedElementAdapter_ThenStillTranslates(string instanceType, string text, Type expected)
    {
        // The FHIRPath evaluator lower-cases instanceType before dispatching on it, so a source
        // that reports non-canonical casing works there. Translation must not be the odd one out.

        // Arrange
        var element = new StubElement { InstanceType = instanceType, Value = text };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.IsType(expected, value);
    }

    [Theory]
    [InlineData("date", "2024-03-05", typeof(P.Date))]
    [InlineData("dateTime", "2024-03-05T13:45:30.123+10:00", typeof(P.DateTime))]
    [InlineData("time", "13:45:30.123", typeof(P.Time))]
    public void GivenIgnixaDateTimeOffsetValue_WhenReadThroughTypedElementAdapter_ThenTranslatesAtTheTypesOwnPrecision(string instanceType, string expectedText, Type expectedType)
    {
        // IElement.Value permits DateTimeOffset as well as the wire string. Each FHIR type has to
        // land at its own precision: date drops the time and offset, time drops the offset, and
        // only dateTime keeps both. The type is asserted as well as the text, because rendering
        // the wire string and calling it done would satisfy the text alone - and that is precisely
        // the pass-through this translation exists to replace.

        // Arrange
        var element = new StubElement
        {
            InstanceType = instanceType,
            Value = new DateTimeOffset(2024, 3, 5, 13, 45, 30, 123, TimeSpan.FromHours(10)),
        };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.IsType(expectedType, value);
        Assert.Equal(expectedText, value?.ToString());
    }

    [Fact]
    public void GivenIgnixaDateTimeValue_WhenReadThroughTypedElementAdapter_ThenTranslatesWithoutFabricatingAnOffset()
    {
        // A bare DateTime carries no offset. Translating it must not invent one, because "+00:00"
        // and "no offset stated" are different instants in FHIR.

        // Arrange
        var element = new StubElement
        {
            InstanceType = "dateTime",
            Value = new DateTime(2024, 3, 5, 13, 45, 30, DateTimeKind.Unspecified),
        };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var dateTime = Assert.IsType<P.DateTime>(value);
        Assert.DoesNotContain("+", dateTime.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Z", dateTime.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("2024-03-05T13:45:30", dateTime.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("007", 7L)]
    [InlineData("+12", 12L)]
    [InlineData("-0", 0L)]
    public void GivenIgnixaInteger64WithNonCanonicalDigits_WhenReadThroughTypedElementAdapter_ThenParsesAndCanonicalises(string text, long expected)
    {
        // Pins a known, accepted consequence rather than desired behaviour. FHIR's integer64
        // grammar is [0]|[-+]?[1-9][0-9]*, so "007" is invalid - but long.TryParse accepts it and
        // canonicalises it to 7, erasing the invalid literal at this boundary. That matches how
        // SchemaAwareElement already parses the narrower integer types, so the shim is consistent
        // with the native path; catching it belongs in the validator, which reads the raw text.

        // Arrange
        var element = new StubElement { InstanceType = "integer64", Value = text };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.Equal(expected, Assert.IsType<long>(value));
    }

    [Fact]
    public void GivenTranslatedTypeCarryingAnUnexpectedClrType_WhenReadThroughTypedElementAdapter_ThenPassesThroughUnchanged()
    {
        // Covers the per-helper fallthrough arms: the instanceType is one we translate, but the
        // value is not a shape that type can be built from. Passing it through unchanged keeps
        // this a translation rather than a coercion.

        // Arrange
        var element = new StubElement { InstanceType = "date", Value = 42 };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void GivenNullIgnixaValue_WhenReadThroughTypedElementAdapter_ThenReturnsNull()
    {
        // Arrange
        var element = new StubElement { InstanceType = "dateTime", Value = null };

        // Act & Assert
        Assert.Null(new TypedElementAdapter(element).Value);
    }

    [Theory]
    [InlineData("dateTime", "2013-01-01T11:22:33+10:00")]
    [InlineData("dateTime", "2013-01-01T11:22:33")]
    [InlineData("dateTime", "2013-01-01T11:22")]
    [InlineData("dateTime", "2013")]
    [InlineData("instant", "2013-01-01T11:22:33.123Z")]
    public void GivenIgnixaFhirTemporalDateTime_WhenReadThroughTypedElementAdapter_ThenReturnsFirelyDateTime(string instanceType, string literal)
    {
        // FhirTemporal is the value type for temporal primitives originating from SchemaAwareElement.
        // TypedElementAdapter.Value must translate it to the Firely type the SDK expects.

        // Arrange
        Assert.True(FhirTemporal.TryParse(literal, FhirPrimitive.DateTime, out var temporal));
        var element = new StubElement { InstanceType = instanceType, Value = temporal };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var dateTime = Assert.IsType<P.DateTime>(value);
        Assert.Equal(literal, dateTime.ToString());
    }

    [Fact]
    public void GivenIgnixaFhirTemporalDate_WhenReadThroughTypedElementAdapter_ThenReturnsFirelyDate()
    {
        // Arrange
        Assert.True(FhirTemporal.TryParse("2013-01-01", FhirPrimitive.Date, out var temporal));
        var element = new StubElement { InstanceType = "date", Value = temporal };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var date = Assert.IsType<P.Date>(value);
        Assert.Equal("2013-01-01", date.ToString());
    }

    [Fact]
    public void GivenIgnixaFhirTemporalTime_WhenReadThroughTypedElementAdapter_ThenReturnsFirelyTime()
    {
        // Arrange
        Assert.True(FhirTemporal.TryParse("11:22:33", FhirPrimitive.Time, out var temporal));
        var element = new StubElement { InstanceType = "time", Value = temporal };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var time = Assert.IsType<P.Time>(value);
        Assert.Equal("11:22:33", time.ToString());
    }

    #endregion

    #region Firely -> Ignixa

    [Theory]
    [InlineData("2013-01-01T11:22:33+10:00")]
    [InlineData("2013-01-01T11:22:33")]
    [InlineData("2013-01-01T11:22")]
    [InlineData("2013")]
    public void GivenFirelyDateTime_WhenReadThroughIgnixaElementAdapter_ThenReturnsFhirTemporal(string text)
    {
        // Arrange
        var element = new StubTypedElement { InstanceType = "dateTime", Value = P.DateTime.Parse(text) };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        var temporal = Assert.IsType<FhirTemporal>(value);
        Assert.Equal(text, temporal.Literal);
    }

    [Fact]
    public void GivenFirelyInstant_WhenReadThroughIgnixaElementAdapter_ThenPreservesInstantKind()
    {
        // Arrange
        var element = new StubTypedElement
        {
            InstanceType = "instant",
            Value = P.DateTime.Parse("2013-01-01T11:22:33.123Z"),
        };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        var temporal = Assert.IsType<FhirTemporal>(value);
        Assert.Equal(FhirPrimitive.Instant, temporal.Kind);
    }

    /// <remarks>
    /// The <c>Kind</c> assertion above pins the mechanism; this pins the consequence. A value read
    /// through the adapter and re-wrapped as a FHIRPath constant is typed by
    /// <c>FhirPathEvaluator.GetFhirPathTypeName</c>, which is the only place <c>Kind</c> distinguishes
    /// <c>Instant</c> from <c>DateTime</c>, and is also what stamps SQL-on-FHIR column types. Losing
    /// the kind at the adapter seam silently retypes such values as <c>dateTime</c>.
    /// </remarks>
    [Fact]
    public void GivenFirelyInstant_WhenAdapterValueIsEvaluatedAsFhirPathConstant_ThenTypedAsInstant()
    {
        // Arrange
        var element = new StubTypedElement
        {
            InstanceType = "instant",
            Value = P.DateTime.Parse("2013-01-01T11:22:33.123Z"),
        };
        var adapted = new IgnixaElementAdapter(element);
        var constant = new ConstantExpression(adapted.Value!);

        // Act
        var results = new FhirPathEvaluator().Evaluate(adapted, constant).ToList();

        // Assert
        Assert.Equal("instant", Assert.Single(results).InstanceType);
    }

    [Fact]
    public void GivenFirelyDate_WhenReadThroughIgnixaElementAdapter_ThenReturnsFhirTemporal()
    {
        // Arrange
        var element = new StubTypedElement { InstanceType = "date", Value = P.Date.Parse("2013-01-01") };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        var temporal = Assert.IsType<FhirTemporal>(value);
        Assert.Equal("2013-01-01", temporal.Literal);
    }

    [Fact]
    public void GivenFirelyTime_WhenReadThroughIgnixaElementAdapter_ThenReturnsFhirTemporal()
    {
        // Arrange
        var element = new StubTypedElement { InstanceType = "time", Value = P.Time.Parse("11:22:33") };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        var temporal = Assert.IsType<FhirTemporal>(value);
        Assert.Equal("11:22:33", temporal.Literal);
    }

    [Fact]
    public void GivenFirelyNonTemporalPrimitives_WhenReadThroughIgnixaElementAdapter_ThenPassesThroughUnchanged()
    {
        // Arrange & Act & Assert
        Assert.Equal("hello", new IgnixaElementAdapter(new StubTypedElement { InstanceType = "string", Value = "hello" }).Value);
        Assert.Equal(true, new IgnixaElementAdapter(new StubTypedElement { InstanceType = "boolean", Value = true }).Value);
        Assert.Equal(42, new IgnixaElementAdapter(new StubTypedElement { InstanceType = "integer", Value = 42 }).Value);
        Assert.Equal(1.5m, new IgnixaElementAdapter(new StubTypedElement { InstanceType = "decimal", Value = 1.5m }).Value);
    }

    [Fact]
    public void GivenFirelyInteger64_WhenReadThroughIgnixaElementAdapter_ThenStaysALongRatherThanBecomingAString()
    {
        // Pins the deliberate asymmetry: integer64 is translated in the Ignixa -> Firely direction
        // only. Ignixa's evaluator treats long as a first-class numeric alongside int and decimal
        // (FhirPathEvaluator.cs:1074-1075, :1317-1318), so "fixing" the asymmetry by stringifying
        // here would silently downgrade numeric comparison to lexical - making 9 > 10 true.
        // Without this test that change passes the whole suite.

        // Arrange
        var element = new StubTypedElement { InstanceType = "integer64", Value = 9007199254740993L };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        Assert.Equal(9007199254740993L, Assert.IsType<long>(value));
    }

    [Fact]
    public void GivenFirelyQuantityValue_WhenReadThroughIgnixaElementAdapter_ThenReturnsIgnixaQuantity()
    {
        // Firely surfaces Quantity as Hl7.Fhir.ElementModel.Types.Quantity; Ignixa's canonical
        // quantity is Ignixa.Abstractions.FhirQuantity. Roughly forty sites across the evaluator and
        // its function libraries reach that type by testing `element.Value is FhirQuantity`
        // straight off IElement.Value, so an untranslated P.Quantity misses every one of them at
        // once - equality, equivalence, ordering, arithmetic and aggregation all silently degrade
        // to an empty collection. Translating here is what closes all of them together.

        // Arrange
        var quantity = new P.Quantity(5m, "mg");
        var element = new StubTypedElement { InstanceType = "Quantity", Value = quantity };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        var translated = Assert.IsType<IgnixaQuantity>(value);
        Assert.Equal(5m, translated.Value);
        Assert.Equal("mg", translated.Unit);
    }

    [Fact]
    public void GivenIgnixaQuantityValue_WhenReadThroughTypedElementAdapter_ThenReturnsFirelyQuantity()
    {
        // The mirror direction, asserted separately because the two adapters translate through
        // different methods and the asymmetry that integer64 documents is independently
        // reintroducible here.

        // Arrange
        var element = new StubElement { InstanceType = "Quantity", Value = new IgnixaQuantity(5m, "mg") };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        var translated = Assert.IsType<P.Quantity>(value);
        Assert.Equal(5m, translated.Value);
        Assert.Equal("mg", translated.Unit);
    }

    [Fact]
    public void GivenWireQuantityElement_WhenReadThroughTypedElementAdapter_ThenValueIsNotSynthesised()
    {
        // A Quantity read off the wire is a complex element: it reports InstanceType "Quantity" but
        // carries no primitive value at all, holding value/unit/code as children instead. The
        // quantity arm of ToFirely is therefore keyed on the CLR type rather than on InstanceType;
        // keying it on the name would try to translate this null.

        // Arrange
        var element = new StubElement { InstanceType = "Quantity", Value = null };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.Null(value);
    }

    #endregion

    #region Round trip

    [Theory]
    [InlineData("dateTime", "2013-01-01T11:22:33+10:00")]
    [InlineData("dateTime", "2013-01-01T11:22:33")]
    [InlineData("dateTime", "2013-01-01T11:22")]
    [InlineData("dateTime", "2013")]
    [InlineData("date", "2013-01-01")]
    [InlineData("time", "11:22:33")]
    public void GivenIgnixaTemporalString_WhenRoundTrippedThroughBothAdapters_ThenPrecisionIsPreserved(string instanceType, string text)
    {
        // Precision matters: a year-only dateTime must not be widened into a full timestamp, and a
        // timezone-less dateTime must not acquire one -- the adapters sit either side of a Firely type
        // that resolves everything to an offset, so an invented "Z" here would be invisible until it
        // reached FHIRPath comparison and turned an indeterminate result into a definite one.
        // Note this case is weak on its own - a pass-through adapter satisfies it too, because the
        // identity function trivially round-trips. The DateTimeOffset theory below is the one with
        // mutation-detection power; this one guards against a lossy parse/render pair.

        // Arrange
        var element = new StubElement { InstanceType = instanceType, Value = text };

        // Act - deliberately re-wrap rather than relying on the unwrapping fast path.
        var firelyValue = new TypedElementAdapter(element).Value;
        var backToIgnixa = new IgnixaElementAdapter(new StubTypedElement { InstanceType = instanceType, Value = firelyValue }).Value;

        // Assert
        Assert.Equal(text, Assert.IsType<FhirTemporal>(backToIgnixa).Literal);
    }

    [Theory]
    [InlineData("date", "2024-03-05")]
    [InlineData("dateTime", "2024-03-05T13:45:30.123+10:00")]
    [InlineData("time", "13:45:30.123")]
    public void GivenIgnixaDateTimeOffset_WhenRoundTrippedThroughBothAdapters_ThenNarrowsToTheTypesWireFormat(string instanceType, string expected)
    {
        // Unlike the string theory above, this one fails against a pass-through adapter: a raw
        // DateTimeOffset would come back as its own ToString(), never as the FHIR wire format,
        // and a date would arrive carrying a time and offset it is not allowed to have.

        // Arrange
        var element = new StubElement
        {
            InstanceType = instanceType,
            Value = new DateTimeOffset(2024, 3, 5, 13, 45, 30, 123, TimeSpan.FromHours(10)),
        };

        // Act
        var firelyValue = new TypedElementAdapter(element).Value;
        var backToIgnixa = new IgnixaElementAdapter(new StubTypedElement { InstanceType = instanceType, Value = firelyValue }).Value;

        // Assert
        Assert.IsNotType<string>(firelyValue);
        Assert.Equal(expected, Assert.IsType<FhirTemporal>(backToIgnixa).Literal);
    }

    #endregion

    #region FHIRPath regression

    [Fact]
    public void GivenFirelyBackedElement_WhenComparingDatesInFhirPath_ThenYieldsBooleanRatherThanEmpty()
    {
        // This is the defect the translation exists to prevent. Ignixa's comparison helpers narrow
        // their operands through a string/DateTime/DateTimeOffset switch; an untranslated Firely
        // P.DateTime falls through to null, the comparison returns empty instead of a boolean, and
        // FHIRPath's empty-propagation carries that all the way out without an error.

        // Arrange
        var birthDate = new StubTypedElement { Name = "birthDate", InstanceType = "date", Value = P.Date.Parse("1990-05-04") };
        var patient = new StubTypedElement
        {
            Name = "Patient",
            InstanceType = "Patient",
            ChildElements = { birthDate },
        };

        var ast = new FhirPathParser().Parse("birthDate > @1980-01-01");

        // Act
        var results = new FhirPathEvaluator()
            .Evaluate(new IgnixaElementAdapter(patient), ast)
            .ToList();

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(true, result.Value);
    }

    [Theory]
    [InlineData("value = 5 'mg'", true)]
    [InlineData("value > 1 'mg'", true)]
    [InlineData("value ~ 5 'mg'", true)]
    [InlineData("value !~ 5 'mg'", false)]
    public void GivenFirelyBackedElement_WhenComparingQuantitiesInFhirPath_ThenYieldsExpectedBoolean(
        string expression,
        bool expected)
    {
        // The quantity counterpart of the date defect above, and the reason the translation has to
        // produce Ignixa's own Quantity rather than be matched for structurally in one helper:
        // equality, ordering and equivalence are three separate call sites that each test
        // `is FhirQuantity` independently, and an untranslated P.Quantity misses all three.

        // Arrange
        var value = new StubTypedElement { Name = "value", InstanceType = "Quantity", Value = new P.Quantity(5m, "mg") };
        var observation = new StubTypedElement
        {
            Name = "Observation",
            InstanceType = "Observation",
            ChildElements = { value },
        };

        var ast = new FhirPathParser().Parse(expression);

        // Act
        var results = new FhirPathEvaluator()
            .Evaluate(new IgnixaElementAdapter(observation), ast)
            .ToList();

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(expected, result.Value);
    }

    #endregion

    #region Memoization

    [Fact]
    public void GivenRepeatedValueReads_WhenReadThroughTypedElementAdapter_ThenTranslatesOnlyOnce()
    {
        // Translation parses, which costs roughly 20x what Firely's own ITypedElement.Value costs.
        // Firely's engines read Value several times per element, and Children() hands out a fresh
        // adapter per call, so nothing upstream would amortise a per-read parse.

        // Arrange
        var element = new CountingStubElement { InstanceType = "dateTime", Value = "2013-01-01T11:22:33+10:00" };
        var adapter = new TypedElementAdapter(element);

        // Act
        _ = adapter.Value;
        _ = adapter.Value;
        _ = adapter.Value;

        // Assert
        Assert.Equal(1, element.ValueReadCount);
    }

    [Fact]
    public void GivenRepeatedValueReads_WhenReadThroughIgnixaElementAdapter_ThenTranslatesOnlyOnce()
    {
        // Arrange
        var element = new CountingStubTypedElement { InstanceType = "date", Value = P.Date.Parse("2013-01-01") };
        var adapter = new IgnixaElementAdapter(element);

        // Act
        _ = adapter.Value;
        _ = adapter.Value;
        _ = adapter.Value;

        // Assert
        Assert.Equal(1, element.ValueReadCount);
    }

    [Fact]
    public void GivenNullValue_WhenReadRepeatedlyThroughTypedElementAdapter_ThenStillTranslatesOnlyOnce()
    {
        // Guards the classic memoization bug of using a null field as the "not yet resolved"
        // sentinel, which would re-translate forever whenever the real answer is null.

        // Arrange
        var element = new CountingStubElement { InstanceType = "dateTime", Value = null };
        var adapter = new TypedElementAdapter(element);

        // Act
        _ = adapter.Value;
        _ = adapter.Value;

        // Assert
        Assert.Equal(1, element.ValueReadCount);
    }

    [Fact]
    public void GivenNullValue_WhenReadRepeatedlyThroughIgnixaElementAdapter_ThenStillTranslatesOnlyOnce()
    {
        // The same guard as above, asserted independently: each adapter carries its own flag, so
        // the null-sentinel bug is independently reintroducible in this one.

        // Arrange
        var element = new CountingStubTypedElement { InstanceType = "date", Value = null };
        var adapter = new IgnixaElementAdapter(element);

        // Act
        _ = adapter.Value;
        _ = adapter.Value;

        // Assert
        Assert.Equal(1, element.ValueReadCount);
    }

    #endregion

    #region Extension method entry points

    [Fact]
    public void GivenIgnixaElement_WhenConvertedViaToTypedElement_ThenValueIsTranslated()
    {
        // Most callers reach the adapters through the extension methods rather than constructing
        // them directly, so the translation has to be live on that path too.

        // Arrange
        var element = new StubElement { InstanceType = "date", Value = "2013-01-01" };

        // Act
        var value = element.ToTypedElement().Value;

        // Assert
        Assert.IsType<P.Date>(value);
    }

    [Fact]
    public void GivenFirelyElement_WhenConvertedViaToIgnixaElement_ThenValueIsTranslated()
    {
        // Arrange
        var element = new StubTypedElement { InstanceType = "date", Value = P.Date.Parse("2013-01-01") };

        // Act
        var value = element.ToIgnixaElement().Value;

        // Assert
        Assert.Equal("2013-01-01", Assert.IsType<FhirTemporal>(value).Literal);
    }

    [Fact]
    public void GivenAdaptedFirelyElement_WhenConvertedBackViaToTypedElement_ThenUnwrapsRatherThanDoubleTranslating()
    {
        // The unwrap fast path returns the original Firely element, whose Value was never
        // translated. If it ever stopped unwrapping, the value would be rendered to a string on the
        // way in and re-parsed on the way out - lossy for anything the string form cannot carry.

        // Arrange
        var original = new StubTypedElement { InstanceType = "date", Value = P.Date.Parse("2013-01-01") };

        // Act
        var roundTripped = original.ToIgnixaElement().ToTypedElement();

        // Assert
        Assert.Same(original, roundTripped);
    }

    #endregion

    #region Stubs

    private sealed class StubElement : IElement
    {
        public string Name { get; init; } = string.Empty;

        public object? Value { get; init; }

        public string InstanceType { get; init; } = string.Empty;

        public string Location { get; init; } = string.Empty;

        public IType? Type { get; init; }

        public bool HasPrimitiveValue => Value != null;

        public List<IElement> ChildElements { get; init; } = [];

        public IReadOnlyList<IElement> Children(string? name = null) =>
            name == null ? ChildElements : ChildElements.Where(c => c.Name == name).ToArray();

        public T? Meta<T>()
            where T : class => null;
    }

    private sealed class CountingStubElement : IElement
    {
        private object? _value;

        public int ValueReadCount { get; private set; }

        public string Name { get; init; } = string.Empty;

        public object? Value
        {
            get
            {
                ValueReadCount++;
                return _value;
            }

            init => _value = value;
        }

        public string InstanceType { get; init; } = string.Empty;

        public string Location { get; init; } = string.Empty;

        public IType? Type { get; init; }

        public bool HasPrimitiveValue => _value != null;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>()
            where T : class => null;
    }

    private sealed class StubTypedElement : ITypedElement
    {
        public string Name { get; init; } = string.Empty;

        public object? Value { get; init; }

        public string? InstanceType { get; init; }

        public string Location { get; init; } = string.Empty;

        public IElementDefinitionSummary? Definition { get; init; }

        public List<ITypedElement> ChildElements { get; init; } = [];

        public IEnumerable<ITypedElement> Children(string? name = null) =>
            name == null ? ChildElements : ChildElements.Where(c => c.Name == name);

        public T? Annotation<T>()
            where T : class => null;
    }

    private sealed class CountingStubTypedElement : ITypedElement
    {
        private object? _value;

        public int ValueReadCount { get; private set; }

        public string Name { get; init; } = string.Empty;

        public object? Value
        {
            get
            {
                ValueReadCount++;
                return _value;
            }

            init => _value = value;
        }

        public string? InstanceType { get; init; }

        public string Location { get; init; } = string.Empty;

        public IElementDefinitionSummary? Definition { get; init; }

        public IEnumerable<ITypedElement> Children(string? name = null) => [];

        public T? Annotation<T>()
            where T : class => null;
    }

    #endregion
}
