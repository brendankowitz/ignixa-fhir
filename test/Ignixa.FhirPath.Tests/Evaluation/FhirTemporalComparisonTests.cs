/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for typed temporal comparison in the FHIRPath evaluator.
 *
 * These cover the wiring that lets the evaluator's date/time comparison helpers
 * consume the typed FhirTemporal value, and assert that the FHIRPath result is
 * the same definite answer regardless of whether the operands originate as
 * resource elements or literals.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class FhirTemporalComparisonTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenTemporalDateAndEqualDateLiteral_WhenEquality_ThenReturnsTrue()
    {
        // Arrange
        var root = CreateTemporalElement("birthDate", "1974-12-25", FhirPrimitive.Date, "date");
        var expr = _parser.Parse("$this = @1974-12-25");

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenTwoTemporalDates_WhenEarlierComparedToLater_ThenReturnsTrue()
    {
        // Arrange
        var start = CreateTemporalElement("start", "2020-01-01", FhirPrimitive.Date, "date");
        var end = CreateTemporalElement("end", "2021-06-15", FhirPrimitive.Date, "date");
        var root = new ContainerElement("Period", new[] { start, end });
        var expr = _parser.Parse("start < end");

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenTemporalDateAndOverlappingDateTimeLiteral_WhenEquality_ThenReturnsEmpty()
    {
        // Arrange
        // A day-precision date overlaps any instant on that day, so the comparison is
        // indeterminate and FHIRPath returns an empty collection rather than false.
        var root = CreateTemporalElement("birthDate", "2024-01-01", FhirPrimitive.Date, "date");
        var expr = _parser.Parse("$this = @2024-01-01T10:00:00");

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTwoDateTimeLiteralsWithDifferentSubSecondPrecision_WhenInequality_ThenReturnsTrue()
    {
        // Arrange
        // FHIRPath treats seconds and sub-seconds as one precision, so these denote different
        // instants and are not equal regardless of how the operands are supplied.
        var root = CreateTemporalElement("birthDate", "1974-12-25", FhirPrimitive.Date, "date");
        var expr = _parser.Parse("@2012-04-15T15:30:31 != @2012-04-15T15:30:31.1");

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenTemporalSecondPrecisionAndSubSecondLiteral_WhenInequality_ThenReturnsTrue()
    {
        // Arrange
        // The second-precision element vs a non-zero sub-second literal must yield a definite
        // answer on the typed path, not empty: seconds and milliseconds are one precision tier.
        var root = CreateTemporalElement("effectiveDateTime", "2024-01-01T10:00:00", FhirPrimitive.DateTime, "dateTime");
        var expr = _parser.Parse("$this != @2024-01-01T10:00:00.5");

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenTemporalSecondPrecisionAndSubSecondLiteral_WhenLessThan_ThenReturnsTrue()
    {
        // Arrange
        var root = CreateTemporalElement("effectiveDateTime", "2024-01-01T10:00:00", FhirPrimitive.DateTime, "dateTime");
        var expr = _parser.Parse("$this < @2024-01-01T10:00:00.5");

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenTwoTemporalDateTimesSecondVersusSubSecond_WhenInequality_ThenReturnsTrue()
    {
        // Arrange
        // Both operands are resource-element temporals: second precision vs non-zero sub-second
        // must still resolve to a definite answer rather than empty.
        var valueDateTime = CreateTemporalElement("valueDateTime", "2024-01-01T10:00:00", FhirPrimitive.DateTime, "dateTime");
        var effectiveDateTime = CreateTemporalElement("effectiveDateTime", "2024-01-01T10:00:00.5", FhirPrimitive.DateTime, "dateTime");
        var root = new ContainerElement("Observation", new[] { valueDateTime, effectiveDateTime });
        var expr = _parser.Parse("valueDateTime != effectiveDateTime");

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenTwoTemporalDateTimesSecondVersusSubSecond_WhenLessThan_ThenReturnsTrue()
    {
        // Arrange
        var valueDateTime = CreateTemporalElement("valueDateTime", "2024-01-01T10:00:00", FhirPrimitive.DateTime, "dateTime");
        var effectiveDateTime = CreateTemporalElement("effectiveDateTime", "2024-01-01T10:00:00.5", FhirPrimitive.DateTime, "dateTime");
        var root = new ContainerElement("Observation", new[] { valueDateTime, effectiveDateTime });
        var expr = _parser.Parse("valueDateTime < effectiveDateTime");

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    private static IElement CreateTemporalElement(string name, string literal, FhirPrimitive kind, string instanceType)
    {
        if (!FhirTemporal.TryParse(literal, kind, out var temporal) || temporal is null)
        {
            throw new InvalidOperationException($"Failed to parse temporal literal '{literal}'.");
        }

        return new TemporalElement(name, temporal, instanceType);
    }

    private sealed class TemporalElement : IElement
    {
        public TemporalElement(string name, FhirTemporal value, string instanceType)
        {
            Name = name;
            Value = value;
            InstanceType = instanceType;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => Name;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    private sealed class ContainerElement : IElement
    {
        private readonly IReadOnlyList<IElement> _children;

        public ContainerElement(string instanceType, IReadOnlyList<IElement> children)
        {
            InstanceType = instanceType;
            _children = children;
        }

        public string Name => InstanceType;
        public string InstanceType { get; }
        public object? Value => null;
        public string Location => InstanceType;
        public IType? Type => null;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null) =>
            name is null ? _children : _children.Where(child => child.Name == name).ToList();

        public T? Meta<T>() where T : class => null;
    }
}
