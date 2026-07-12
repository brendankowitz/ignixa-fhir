// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

/// <summary>
/// Regression coverage for the specific bug ExpressionRewriter.VisitCompositeComponent's
/// rebuild-if-changed semantics (Task 1) exist to prevent: DateTimeEqualityRewriter rewrites a
/// composite Date component's inner range expression, and the CompositeComponentExpression wrapper
/// around it must survive that rewrite intact. Builds the input tree directly in the containment shape
/// (And(GE(DateTimeStart,x), LE(DateTimeEnd,y))) that DateTimeEqualityRewriter.MatchPattern still
/// recognizes today (the shape the `ap` comparator produces) — NOT via the parser's `eq` comparator,
/// whose output shape changed in commit 23c18854 and no longer matches (tracked as a separate,
/// pre-existing, out-of-scope follow-up; see the design spec).
/// </summary>
public class DateTimeEqualityRewriterCompositeTests
{
    private static SearchParameterInfo CreateTokenDateComposite()
    {
        var tokenComponentDefinition = new SearchParameterInfo("code", "code", SearchParamType.Token);
        var dateComponentDefinition = new SearchParameterInfo("value-date", "value-date", SearchParamType.Date);

        return new SearchParameterInfo(
            "code-value-date",
            "code-value-date",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = tokenComponentDefinition },
                new SearchParameterComponentInfo { ResolvedSearchParameter = dateComponentDefinition },
            ]);
    }

    [Fact]
    public void GivenCompositeContainmentShapedDateRange_WhenDateTimeEqualityRewriterRuns_ThenDateComponentWrapperSurvivesRewrite()
    {
        var composite = CreateTokenDateComposite();
        var tokenComponent = new CompositeComponentExpression(
            composite.Component[0].ResolvedSearchParameter,
            0,
            Expression.StringEquals(FieldName.TokenCode, 0, "8480-6", false));
        var dateComponent = new CompositeComponentExpression(
            composite.Component[1].ResolvedSearchParameter,
            1,
            Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, 1, new DateTime(2020, 6, 1)),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, 1, new DateTime(2020, 6, 1, 23, 59, 59))));
        var parsed = Expression.SearchParameter(composite, Expression.And(tokenComponent, dateComponent));

        var rewritten = (SearchParameterExpression)DateTimeEqualityRewriter.Instance.VisitSearchParameter(parsed, context: null);

        var and = (MultiaryExpression)rewritten.Expression;
        var rewrittenDateComponent = and.Expressions.OfType<CompositeComponentExpression>().Single(c => c.Position == 1);

        // Still wrapped (not stripped) and still carries its Date identity.
        rewrittenDateComponent.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Date);

        // The rewrite fired: the inner expression grew from 2 to 3 range-bound expressions.
        var innerAnd = (MultiaryExpression)rewrittenDateComponent.WrappedExpression;
        innerAnd.Expressions.Count.ShouldBe(3);
    }
}
