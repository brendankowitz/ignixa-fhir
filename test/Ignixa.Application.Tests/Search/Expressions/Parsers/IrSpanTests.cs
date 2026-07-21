// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class IrSpanTests
{
    private static readonly string[] Patient = ["Patient"];

    [Fact]
    public void GivenAParsedPredicate_WhenInspected_ThenItsSpanExtractsTheValueText()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        const string value = "Smith";

        var parsed = (SearchParameterExpression)context.Parser.Parse(Patient, "name", value);
        var predicate = (SearchParameterPredicateExpression)parsed.Expression;

        predicate.Span.ShouldNotBeNull();
        value.Substring(predicate.Span!.Value.Start, predicate.Span.Value.Length).ShouldBe("Smith");
    }

    [Fact]
    public void GivenTwoPredicatesDifferingOnlyBySpan_WhenComparedValueInsensitively_ThenTheyMatch()
    {
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://x/name"));
        var a = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, null, new StringSearchValue("s"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 1),
        };
        var b = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, null, new StringSearchValue("s"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 7, 1),
        };

        a.ValueInsensitiveEquals(b).ShouldBeTrue();
        a.ToString().ShouldBe(b.ToString());
    }

    [Fact]
    public void GivenACompositeComponent_WhenRebuiltByARewriter_ThenTheSpanSurvives()
    {
        var parameter = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://x/code"));
        var inner = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, null, new TokenSearchValue(null, "abc", null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 2, 3),
        };
        var component = new CompositeComponentExpression(parameter, 0, inner)
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 6),
        };

        var rewritten = (CompositeComponentExpression)component.AcceptVisitor(
            new ReplacingRewriter(), context: null);

        ReferenceEquals(rewritten, component).ShouldBeFalse();
        rewritten.Span.ShouldBe(component.Span);
    }

    /// <summary>Returns a fresh inner instance so the rebuild path is actually taken.</summary>
    private sealed class ReplacingRewriter : ExpressionRewriter<object?>
    {
        public override Expression VisitSearchParameterPredicate(
            SearchParameterPredicateExpression expression, object? context)
            => new SearchParameterPredicateExpression(
                expression.Parameter, expression.Comparator, expression.Modifier, expression.Value)
            {
                Span = expression.Span,
            };
    }
}
