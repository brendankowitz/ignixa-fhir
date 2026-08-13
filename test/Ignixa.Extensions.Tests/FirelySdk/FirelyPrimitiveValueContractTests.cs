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

    [Fact]
    public void GivenUnparseableIgnixaDateTime_WhenReadThroughTypedElementAdapter_ThenReturnsRawTextWithoutThrowing()
    {
        // Firely's own PocoElementNode degrades this way, so navigation over a malformed
        // resource stays possible instead of throwing mid-traversal.

        // Arrange
        var element = new StubElement { InstanceType = "dateTime", Value = "not-a-date" };

        // Act
        var value = new TypedElementAdapter(element).Value;

        // Assert
        Assert.Equal("not-a-date", value);
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

    #endregion

    #region Round trip

    [Theory]
    [InlineData("dateTime", "2013-01-01T11:22:33+10:00")]
    [InlineData("dateTime", "2013")]
    [InlineData("date", "2013-01-01")]
    [InlineData("time", "11:22:33")]
    public void GivenIgnixaTemporal_WhenRoundTrippedThroughBothAdapters_ThenPrecisionIsPreserved(string instanceType, string text)
    {
        // Precision matters: a year-only dateTime must not be widened into a full timestamp.

        // Arrange
        var element = new StubElement { InstanceType = instanceType, Value = text };

        // Act — deliberately re-wrap rather than relying on the unwrapping fast path.
        var firelyValue = new TypedElementAdapter(element).Value;
        var backToIgnixa = new IgnixaElementAdapter(new StubTypedElement { InstanceType = instanceType, Value = firelyValue }).Value;

        // Assert
        Assert.Equal(text, backToIgnixa);
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

    #region Stubs

    private sealed class StubElement : IElement
    {
        public string Name { get; init; } = string.Empty;

        public object? Value { get; init; }

        public string InstanceType { get; init; } = string.Empty;

        public string Location { get; init; } = string.Empty;

        public IType? Type { get; init; }

        public bool HasPrimitiveValue => Value != null;

        public List<IElement> ChildElements { get; init; } = new();

        public IReadOnlyList<IElement> Children(string? name = null) =>
            name == null ? ChildElements : ChildElements.Where(c => c.Name == name).ToArray();

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

        public List<ITypedElement> ChildElements { get; init; } = new();

        public IEnumerable<ITypedElement> Children(string? name = null) =>
            name == null ? ChildElements : ChildElements.Where(c => c.Name == name);

        public T? Annotation<T>()
            where T : class => null;
    }

    #endregion
}
