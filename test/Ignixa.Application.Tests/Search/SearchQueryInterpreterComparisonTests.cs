// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

/// <summary>
/// Covers ComparisonValueVisitor's Number/Quantity/DateTime bound selection through
/// SearchQueryInterpreter's public surface (ComparisonValueVisitor itself is internal). Locks down
/// that gt/ge/lt/le/sa/eb pick the correct stored-side bound - previously Number/Quantity always
/// read .High and DateTime always read .Start regardless of operator.
/// </summary>
public class SearchQueryInterpreterComparisonTests
{
    private static readonly SearchParameterInfo NumberParam = new("value-number", "value-number", SearchParamType.Number);

    private static bool Evaluate(BinaryExpression expression, ISearchValue storedValue)
    {
        var interpreter = new SearchQueryInterpreter();
        var context = new SearchQueryInterpreter.Context { ParameterName = "value-number" };
        var predicate = interpreter.VisitBinary(expression, context);

        var resourceKey = new ResourceKey("Observation", "obs-1");
        var index = new SearchIndexEntry[] { new(NumberParam, storedValue) };
        var input = new (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)[]
        {
            (resourceKey, index)
        };

        return predicate(input).Any();
    }

    [Theory]
    [InlineData(BinaryOperator.GreaterThan, 15, true)]      // High(20) > 15
    [InlineData(BinaryOperator.GreaterThan, 25, false)]     // High(20) > 25 is false
    [InlineData(BinaryOperator.GreaterThanOrEqual, 20, true)]
    [InlineData(BinaryOperator.LessThan, 15, true)]         // Low(10) < 15
    [InlineData(BinaryOperator.LessThan, 5, false)]         // Low(10) < 5 is false
    [InlineData(BinaryOperator.LessThanOrEqual, 10, true)]
    [InlineData(BinaryOperator.StartsAfter, 5, true)]       // Low(10) > 5
    [InlineData(BinaryOperator.StartsAfter, 15, false)]     // distinguishes Sa from Gt
    [InlineData(BinaryOperator.EndsBefore, 25, true)]       // High(20) < 25
    [InlineData(BinaryOperator.EndsBefore, 15, false)]      // distinguishes Eb from Lt
    public void GivenStoredNumberRange_WhenComparing_ThenMatchesCanonicalSemantics(BinaryOperator op, int searchValue, bool expectMatch)
    {
        var stored = new NumberSearchValue(10m, 20m);
        var expression = new BinaryExpression(op, FieldName.Number, componentIndex: null, (decimal)searchValue);

        Evaluate(expression, stored).ShouldBe(expectMatch);
    }

    [Fact]
    public void GivenOverlappingStoredDateRange_WhenComparingGtAndSa_ThenTheyProduceDifferentResults()
    {
        var start = new PartialDateTime(new DateTimeOffset(2019, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var end = new PartialDateTime(new DateTimeOffset(2020, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var stored = new DateTimeSearchValue(start, end);
        var boundary = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var gt = new BinaryExpression(BinaryOperator.GreaterThan, FieldName.DateTimeEnd, componentIndex: null, boundary);
        var sa = new BinaryExpression(BinaryOperator.StartsAfter, FieldName.DateTimeStart, componentIndex: null, boundary);

        Evaluate(gt, stored).ShouldBeTrue();
        Evaluate(sa, stored).ShouldBeFalse();
    }
}
