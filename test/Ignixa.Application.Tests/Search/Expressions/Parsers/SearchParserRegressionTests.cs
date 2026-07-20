// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.RegularExpressions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

/// <summary>
/// Guards two behaviors that regressed when the parser's canonical output changed from the old
/// field-level tree to the typed predicate IR, and were fixed afterward.
/// </summary>
public class SearchParserRegressionTests
{
    private static readonly string[] Patient = ["Patient"];

    // Regression 1: an unsupported modifier/comparator-for-type must be rejected at PARSE time, so
    // SearchOptionsBuilder's parse-time catch can gracefully ignore the parameter. When validation was
    // deferred to lowering, these surfaced as hard failures at query-execution time instead.
    [Fact]
    public void GivenAModifierUnsupportedForTheValueType_WhenParsed_ThenThrowsInvalidSearchOperationAtParseTime()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "birthdate", SearchParamType.Date);

        Should.Throw<InvalidSearchOperationException>(
            () => context.Parser.Parse(Patient, "birthdate:exact", "2000-01-01"));
    }

    // Regression 2: the parser now emits a typed IR, so LegacyExpressionLowerer.LowerToLegacy is the
    // shared bridge that lowers it back to the old field-level shape every old-shape backend consumes.
    // It stays pure -- the SQL-only date-index optimization is applied by the SQL backend afterward
    // (see DateTimeEqualityRewriterTests in the DataLayer tests), not baked into this shared bridge.
    [Fact]
    public void GivenAnApproximateDateSearch_WhenLoweredToLegacy_ThenItUsesTheContainmentShape()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "birthdate", SearchParamType.Date);

        Expression lowered = LegacyExpressionLowerer.LowerToLegacy(
            context.Parser.Parse(Patient, "birthdate", "ap2000-01-01"));

        // :ap lowers to the containment shape (DateTimeStart >= x AND DateTimeEnd <= y): a single
        // DateTimeStart bound. Any SQL index optimization is layered on separately, not here.
        (lowered.ToString() ?? string.Empty).ShouldContain("FieldGreaterThanOrEqual DateTimeStart");
        DateTimeStartBoundCount(lowered).ShouldBe(1);
    }

    [Fact]
    public void GivenAnEqualityDateSearch_WhenLoweredToLegacy_ThenItUsesTheOverlapShape()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "birthdate", SearchParamType.Date);

        Expression lowered = LegacyExpressionLowerer.LowerToLegacy(
            context.Parser.Parse(Patient, "birthdate", "2000-01-01"));

        // Equality uses the overlap shape (DateTimeStart <= end AND DateTimeEnd >= start).
        (lowered.ToString() ?? string.Empty).ShouldContain("FieldLessThanOrEqual DateTimeStart");
        DateTimeStartBoundCount(lowered).ShouldBe(1);
    }

    private static int DateTimeStartBoundCount(Expression expression)
        => Regex.Matches(expression.ToString() ?? string.Empty, "DateTimeStart").Count;
}
