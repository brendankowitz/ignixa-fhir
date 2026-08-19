/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for the temporal interval-bound implementation used by FHIRPath ordering.
 */

using System.Globalization;
using System.Threading;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class FhirPathDateTimeBoundDelegationTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("@2020 < @2021")]
    [InlineData("@2020-01 < @2020-02")]
    [InlineData("@2020-01-01 < @2020-01-02")]
    [InlineData("@2020-01-01T10:00 < @2020-01-01T10:01")]
    [InlineData("@2020-01-01T10:00:00 < @2020-01-01T10:00:01")]
    [InlineData("@2020-01-01T10:00:00.001 < @2020-01-01T10:00:00.002")]
    public void GivenTemporalLiteralsAtEachPrecision_WhenOrdered_ThenReturnsTrue(string expression)
    {
        // Arrange
        var parsed = _parser.Parse(expression);

        // Act
        var result = _evaluator.Evaluate(Root(), parsed).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenOpenEndedPeriodSentinel_WhenOrderedBeforeAnEarlierYear_ThenReturnsFalse()
    {
        // Arrange
        var period = Parse("""
            {
              "resourceType": "Period",
              "end": "9999-12-31"
            }
            """);
        var expression = _parser.Parse("Period.end < @2020");

        // Act
        var result = _evaluator.Evaluate(period, expression).Single();

        // Assert
        result.Value.ShouldBe(false);
    }

    [Fact]
    public void GivenYearAndMonthLiteralsUnderTurkishCulture_WhenUsedInArithmetic_ThenResultsRemainInvariant()
    {
        // Arrange
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        var previousUiCulture = Thread.CurrentThread.CurrentUICulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("tr-TR");

        try
        {
            var yearExpression = _parser.Parse("@2020 + 1 year");
            var monthExpression = _parser.Parse("@2020-01 + 1 month");

            // Act
            var year = _evaluator.Evaluate(Root(), yearExpression).Single();
            var month = _evaluator.Evaluate(Root(), monthExpression).Single();

            // Assert
            year.Value.ShouldBe("2021");
            month.Value.ShouldBe("2020-02");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
            Thread.CurrentThread.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void GivenOverlappingPartialPrecisionLiterals_WhenOrdered_ThenReturnsEmpty()
    {
        // Arrange
        var expression = _parser.Parse("@2020 < @2020-01");

        // Act
        var result = _evaluator.Evaluate(Root(), expression).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    private static IElement Root() => new FunctionHelpers.PrimitiveElement(0, "integer");

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);
}
