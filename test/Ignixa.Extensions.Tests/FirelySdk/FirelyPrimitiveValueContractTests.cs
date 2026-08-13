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
using Ignixa.FhirPath.Parser;
using Xunit;
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

        // Assert
        Assert.Equal(text, value);
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
    [InlineData("DateTime", "2013-01-01T11:22:33+10:00")]
    [InlineData("DATE", "2013-01-01")]
    [InlineData("Time", "11:22:33")]
    [InlineData("Integer64", "42")]
    public void GivenInstanceTypeInUnexpectedCasing_WhenReadThroughTypedElementAdapter_ThenStillTranslates(string instanceType, string text)
    {
        // The FHIRPath evaluator lower-cases instanceType before dispatching on it, so a source
        // that reports non-canonical casing works there. Translation must not be the odd one out.

        // Arrange
        var element = new StubElement { InstanceType = instanceType, Value = text };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.IsNotType<string>(value);
    }

    [Theory]
    [InlineData("date", "2024-03-05")]
    [InlineData("dateTime", "2024-03-05T13:45:30.123+10:00")]
    [InlineData("time", "13:45:30.123")]
    public void GivenIgnixaDateTimeOffsetValue_WhenReadThroughTypedElementAdapter_ThenTranslatesAtTheTypesOwnPrecision(string instanceType, string expected)
    {
        // IElement.Value permits DateTimeOffset as well as the wire string. Each FHIR type has to
        // land at its own precision: date drops the time and offset, time drops the offset, and
        // only dateTime keeps both.

        // Arrange
        var element = new StubElement
        {
            InstanceType = instanceType,
            Value = new DateTimeOffset(2024, 3, 5, 13, 45, 30, 123, TimeSpan.FromHours(10)),
        };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.Equal(expected, value?.ToString());
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

    [Fact]
    public void GivenNullIgnixaValue_WhenReadThroughTypedElementAdapter_ThenReturnsNull()
    {
        // Arrange
        var element = new StubElement { InstanceType = "dateTime", Value = null };

        // Act & Assert
        Assert.Null(new TypedElementAdapter(element).Value);
    }

    #endregion

    #region Firely -> Ignixa

    [Theory]
    [InlineData("2013-01-01T11:22:33+10:00")]
    [InlineData("2013")]
    public void GivenFirelyDateTime_WhenReadThroughIgnixaElementAdapter_ThenReturnsWireFormatString(string text)
    {
        // Arrange
        var element = new StubTypedElement { InstanceType = "dateTime", Value = P.DateTime.Parse(text) };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        Assert.Equal(text, Assert.IsType<string>(value));
    }

    [Fact]
    public void GivenFirelyDate_WhenReadThroughIgnixaElementAdapter_ThenReturnsWireFormatString()
    {
        // Arrange
        var element = new StubTypedElement { InstanceType = "date", Value = P.Date.Parse("2013-01-01") };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        Assert.Equal("2013-01-01", Assert.IsType<string>(value));
    }

    [Fact]
    public void GivenFirelyTime_WhenReadThroughIgnixaElementAdapter_ThenReturnsWireFormatString()
    {
        // Arrange
        var element = new StubTypedElement { InstanceType = "time", Value = P.Time.Parse("11:22:33") };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        Assert.Equal("11:22:33", Assert.IsType<string>(value));
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
    public void GivenFirelyQuantityValue_WhenReadThroughIgnixaElementAdapter_ThenPassesThroughUntranslated()
    {
        // Pins a known gap rather than asserting desired behaviour.
        //
        // Firely surfaces Quantity as Hl7.Fhir.ElementModel.Types.Quantity. Ignixa's evaluator
        // tests `element.Value is Ignixa.FhirPath.Types.Quantity` (FhirPathEvaluator.cs:649), a
        // different type, so it never matches. The consequence is silent: on an adapted Firely
        // element, `value.toQuantity() = 5 'mg'` and `value.toQuantity() > 1 'mg'` both yield an
        // empty collection instead of a boolean.
        //
        // This shim cannot fix it. Ignixa.FhirPath.Types.Quantity lives in Ignixa.FhirPath, which
        // Ignixa.Extensions.FirelySdk6 deliberately does not reference - translating here would
        // invert the layering. The fix belongs either in Ignixa.FhirPath (accept P.Quantity) or in
        // moving Quantity down to Ignixa.Abstractions. Update this test when that lands.

        // Arrange
        var quantity = new P.Quantity(5m, "mg");
        var element = new StubTypedElement { InstanceType = "Quantity", Value = quantity };

        // Act
        var value = new IgnixaElementAdapter(element).Value;

        // Assert
        Assert.Same(quantity, value);
    }

    #endregion

    #region Round trip

    [Theory]
    [InlineData("dateTime", "2013-01-01T11:22:33+10:00")]
    [InlineData("dateTime", "2013")]
    [InlineData("date", "2013-01-01")]
    [InlineData("time", "11:22:33")]
    public void GivenIgnixaTemporalString_WhenRoundTrippedThroughBothAdapters_ThenPrecisionIsPreserved(string instanceType, string text)
    {
        // Precision matters: a year-only dateTime must not be widened into a full timestamp.
        // Note this case is weak on its own - a pass-through adapter satisfies it too, because the
        // identity function trivially round-trips. The DateTimeOffset theory below is the one with
        // mutation-detection power; this one guards against a lossy parse/render pair.

        // Arrange
        var element = new StubElement { InstanceType = instanceType, Value = text };

        // Act - deliberately re-wrap rather than relying on the unwrapping fast path.
        var firelyValue = new TypedElementAdapter(element).Value;
        var backToIgnixa = new IgnixaElementAdapter(new StubTypedElement { InstanceType = instanceType, Value = firelyValue }).Value;

        // Assert
        Assert.Equal(text, backToIgnixa);
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
        Assert.Equal(expected, Assert.IsType<string>(backToIgnixa));
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
        Assert.Equal("2013-01-01", Assert.IsType<string>(value));
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
