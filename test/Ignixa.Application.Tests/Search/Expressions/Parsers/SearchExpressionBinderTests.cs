// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchExpressionBinderTests
{
    private static readonly string[] PatientTargets = ["Patient"];

    [Fact]
    public void GivenStringSyntax_WhenBinding_ThenUsesCanonicalStringParser()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Patient", "name", SearchParamType.String);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.String,
            null,
            @"Smith\,Jones");

        var result = binder.BindValue(parameter, null, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var value = search.Expression.ShouldBeOfType<StringExpression>();
        value.Value.ShouldBe("Smith,Jones");
        value.StringOperator.ShouldBe(StringOperator.StartsWith);
    }

    [Fact]
    public void GivenGreaterThanNumberSyntax_WhenBinding_ThenBuildsExistingComparatorExpression()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Observation", "value-number", SearchParamType.Number);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Number,
            null,
            "gt120");

        var result = binder.BindValue(parameter, null, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var comparison = search.Expression.ShouldBeOfType<BinaryExpression>();
        comparison.BinaryOperator.ShouldBe(BinaryOperator.GreaterThan);
        comparison.FieldName.ShouldBe(FieldName.Number);
        comparison.Value.ShouldBe(120m);
    }

    [Fact]
    public void GivenInvalidNumberSyntax_WhenBinding_ThenMapsCanonicalParserError()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Observation", "value-number", SearchParamType.Number);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Number,
            null,
            "not-a-number");

        Should.Throw<BadSearchRequestException>(
            () => binder.BindValue(parameter, null, syntax));
    }

    [Fact]
    public void GivenUntypedReferenceAndTypeModifier_WhenBinding_ThenAppliesReferenceTarget()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add(
            "Observation",
            "subject",
            SearchParamType.Reference,
            targets: PatientTargets);
        var modifier = new SearchModifier(SearchModifierCode.Type, "Patient");
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Reference,
            modifier,
            "123");

        var result = binder.BindValue(parameter, modifier, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var reference = search.Expression.ShouldBeOfType<MultiaryExpression>();
        reference.Expressions
            .OfType<StringExpression>()
            .Single(expression => expression.FieldName == FieldName.ReferenceResourceType)
            .Value.ShouldBe("Patient");
    }

    [Fact]
    public void GivenConflictingReferenceTypeModifier_WhenBinding_ThenRejectsModifier()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add(
            "Observation",
            "subject",
            SearchParamType.Reference,
            targets: PatientTargets);
        var modifier = new SearchModifier(SearchModifierCode.Type, "Patient");
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Reference,
            modifier,
            "Observation/123");

        Should.Throw<InvalidSearchOperationException>(
            () => binder.BindValue(parameter, modifier, syntax));
    }

    [Fact]
    public void GivenTokenAlternatives_WhenBinding_ThenBuildsOrExpression()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Observation", "code", SearchParamType.Token);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Token,
            null,
            "http://loinc.org|a,http://loinc.org|b");

        var result = binder.BindValue(parameter, null, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var alternatives = search.Expression.ShouldBeOfType<MultiaryExpression>();
        alternatives.MultiaryOperation.ShouldBe(MultiaryOperator.Or);
        alternatives.Expressions.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenNotTokenAlternatives_WhenBinding_ThenNegatesTheWholeOr()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Observation", "code", SearchParamType.Token);
        var modifier = new SearchModifier(SearchModifierCode.Not);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Token,
            modifier,
            "http://loinc.org|a,http://loinc.org|b");

        var result = binder.BindValue(parameter, modifier, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var not = search.Expression.ShouldBeOfType<NotExpression>();
        var alternatives = not.Expression.ShouldBeOfType<MultiaryExpression>();
        alternatives.MultiaryOperation.ShouldBe(MultiaryOperator.Or);
        foreach (Expression expression in alternatives.Expressions)
        {
            expression.ShouldNotBeOfType<NotExpression>();
        }
    }

    [Theory]
    [InlineData("gt2026-01-01,2026-02-01")]
    [InlineData("2026-01-01,gt2026-02-01")]
    public void GivenComparatorWithAlternatives_WhenBinding_ThenPreservesComparatorError(string value)
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Observation", "date", SearchParamType.Date);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Date,
            null,
            value);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.BindValue(parameter, null, syntax));

        exception.Message.ShouldBe(Resources.SearchComparatorNotSupported);
    }

    [Fact]
    public void GivenExactStringAlternatives_WhenBinding_ThenAppliesModifierToEveryItem()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Patient", "name", SearchParamType.String);
        var modifier = new SearchModifier(SearchModifierCode.Exact);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.String,
            modifier,
            "Smith,Jones");

        var result = binder.BindValue(parameter, modifier, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var alternatives = search.Expression.ShouldBeOfType<MultiaryExpression>();
        alternatives.Expressions.ShouldAllBe(
            expression => expression.ShouldBeOfType<StringExpression>().StringOperator == StringOperator.Equals);
    }

    [Fact]
    public void GivenCompositeAlternatives_WhenBinding_ThenBuildsOrOfComponentAnds()
    {
        var context = new SearchParserTestContext();
        var code = new SearchParameterInfo("code", "code", SearchParamType.Token);
        var quantity = new SearchParameterInfo(
            "value-quantity",
            "value-quantity",
            SearchParamType.Quantity);
        var codeComponent = new SearchParameterComponentInfo(
            new Uri("http://example.org/SearchParameter/code"))
        {
            ResolvedSearchParameter = code,
        };
        var quantityComponent = new SearchParameterComponentInfo(
            new Uri("http://example.org/SearchParameter/value-quantity"))
        {
            ResolvedSearchParameter = quantity,
        };
        var composite = context.Add(
            "Observation",
            "code-value-quantity",
            SearchParamType.Composite,
            components: [codeComponent, quantityComponent]);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Composite,
            null,
            "http://loinc.org|8480-6$gt120,29463-7$lt80");

        var result = binder.BindValue(composite, null, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var alternatives = search.Expression.ShouldBeOfType<MultiaryExpression>();
        alternatives.MultiaryOperation.ShouldBe(MultiaryOperator.Or);
        alternatives.Expressions.Count.ShouldBe(2);
        alternatives.Expressions.ShouldAllBe(
            expression => expression.ShouldBeOfType<MultiaryExpression>()
                .MultiaryOperation == MultiaryOperator.And);
    }

    [Fact]
    public void GivenTooManyCompositeComponents_WhenBinding_ThenPreservesResourceMessage()
    {
        var context = new SearchParserTestContext();
        var component = new SearchParameterComponentInfo
        {
            ResolvedSearchParameter =
                new SearchParameterInfo("code", "code", SearchParamType.Token),
        };
        var composite = context.Add(
            "Observation",
            "code-value",
            SearchParamType.Composite,
            components: [component]);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Composite,
            null,
            "code$value");

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.BindValue(composite, null, syntax));

        exception.Message.ShouldBe(string.Format(
            Resources.NumberOfCompositeComponentsExceeded,
            composite.Code));
    }

    [Fact]
    public void GivenUnresolvedCompositeComponent_WhenBinding_ThenPreservesResourceMessage()
    {
        var context = new SearchParserTestContext();
        var definitionUrl = new Uri("http://example.org/SearchParameter/code");
        var composite = context.Add(
            "Observation",
            "code-value",
            SearchParamType.Composite,
            components: [new SearchParameterComponentInfo(definitionUrl)]);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Composite,
            null,
            "code");

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.BindValue(composite, null, syntax));

        exception.Message.ShouldBe(string.Format(
            Resources.CompositeSearchParameterComponentNotResolved,
            composite.Code,
            0,
            definitionUrl));
    }

    [Theory]
    [InlineData(SearchModifierCode.Exact, "value")]
    [InlineData(SearchModifierCode.Not, "value,other")]
    public void GivenCompositeWithModifier_WhenBinding_ThenRejectsModifier(
        SearchModifierCode modifierCode,
        string value)
    {
        var context = new SearchParserTestContext();
        SearchParameterInfo composite = CreateSingleComponentComposite(
            context,
            SearchParamType.String);
        var modifier = new SearchModifier(modifierCode);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Composite,
            modifier,
            value);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.BindValue(composite, modifier, syntax));

        exception.Message.ShouldBe(string.Format(
            Resources.ModifierNotSupported,
            modifier,
            composite.Code));
    }

    [Fact]
    public void GivenReferenceShapedCompositeComponent_WhenBinding_ThenInfersReferenceType()
    {
        var context = new SearchParserTestContext();
        SearchParameterInfo composite = CreateSingleComponentComposite(
            context,
            SearchParamType.String);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Composite,
            null,
            "Patient/123");

        var result = binder.BindValue(composite, null, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var components = search.Expression.ShouldBeOfType<MultiaryExpression>();
        var reference = components.Expressions[0].ShouldBeOfType<MultiaryExpression>();
        reference.Expressions
            .OfType<StringExpression>()
            .Single(expression => expression.FieldName == FieldName.ReferenceResourceType)
            .Value.ShouldBe("Patient");
    }

    [Fact]
    public void GivenComparatorShapedStringComponent_WhenBinding_ThenTreatsComparatorAsText()
    {
        var context = new SearchParserTestContext();
        SearchParameterInfo composite = CreateSingleComponentComposite(
            context,
            SearchParamType.String);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.Composite,
            null,
            "gtSmith");

        var result = binder.BindValue(composite, null, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var components = search.Expression.ShouldBeOfType<MultiaryExpression>();
        var value = components.Expressions[0].ShouldBeOfType<StringExpression>();
        value.Value.ShouldBe("gtSmith");
        value.StringOperator.ShouldBe(StringOperator.StartsWith);
    }

    [Fact]
    public void GivenMissingModifier_WhenParsingFacade_ThenBuildsMissingSearchParameter()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Patient", "name", SearchParamType.String);

        var result = context.ValueParser.Parse(
            parameter,
            new SearchModifier(SearchModifierCode.Missing),
            "true");

        result.ShouldBeOfType<MissingSearchParameterExpression>()
            .IsMissing.ShouldBeTrue();
    }

    [Fact]
    public void GivenMissingSyntax_WhenBinding_ThenBuildsMissingSearchParameter()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Patient", "name", SearchParamType.String);
        var modifier = new SearchModifier(SearchModifierCode.Missing);
        var binder = CreateBinder(context);
        var syntax = SearchValueSyntaxParser.Parse(
            SearchParamType.String,
            modifier,
            "true");

        var result = binder.BindValue(parameter, modifier, syntax);

        result.ShouldBeOfType<MissingSearchParameterExpression>()
            .IsMissing.ShouldBeTrue();
    }

    [Fact]
    public void GivenOfTypeAlternatives_WhenParsingFacade_ThenBuildsOr()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Patient", "identifier", SearchParamType.Token);
        var modifier = new SearchModifier(SearchModifierCode.OfType);

        var result = context.ValueParser.Parse(
            parameter,
            modifier,
            "http://terminology.hl7.org|MR|123,http://terminology.hl7.org|SS|456");

        result.ShouldBeOfType<SearchParameterExpression>()
            .Expression.ShouldBeOfType<MultiaryExpression>()
            .MultiaryOperation.ShouldBe(MultiaryOperator.Or);
    }

    [Theory]
    [InlineData(SearchModifierCode.Text, SearchParamType.Token, "display")]
    [InlineData(SearchModifierCode.Above, SearchParamType.Uri, "http://example.org/a")]
    [InlineData(SearchModifierCode.Below, SearchParamType.Uri, "http://example.org/a")]
    public void GivenSupportedSpecialModifier_WhenParsingFacade_ThenBuildsExpression(
        SearchModifierCode modifierCode,
        SearchParamType type,
        string value)
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Resource", "special", type);

        var result = context.ValueParser.Parse(
            parameter,
            new SearchModifier(modifierCode),
            value);

        result.ShouldBeAssignableTo<Expression>();
    }

    [Theory]
    [InlineData(SearchModifierCode.Text, "Smith")]
    [InlineData(SearchModifierCode.OfType, "system|code|value")]
    public void GivenSpecialModifierOnUnsupportedType_WhenParsingFacade_ThenRejectsModifier(
        SearchModifierCode modifierCode,
        string value)
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Patient", "name", SearchParamType.String);
        var modifier = new SearchModifier(modifierCode);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => context.ValueParser.Parse(parameter, modifier, value));

        exception.Message.ShouldBe(string.Format(
            Resources.ModifierNotSupported,
            modifier,
            parameter.Code));
    }

    [Fact]
    public void GivenTextModifierWithComma_WhenParsingFacade_ThenTreatsCommaAsText()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Observation", "code", SearchParamType.Token);

        var result = context.ValueParser.Parse(
            parameter,
            new SearchModifier(SearchModifierCode.Text),
            "alpha,beta");

        result.ShouldBeOfType<SearchParameterExpression>()
            .Expression.ShouldBeOfType<StringExpression>()
            .Value.ShouldBe("alpha,beta");
    }

    [Fact]
    public void GivenReferenceTargetModifier_WhenParsingFacade_ThenAppliesResourceType()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add(
            "Observation",
            "subject",
            SearchParamType.Reference,
            targets: PatientTargets);

        var result = context.ValueParser.Parse(
            parameter,
            new SearchModifier(SearchModifierCode.Type, "Patient"),
            "123");

        result.ToString().ShouldContain("Patient");
    }

    private static SearchExpressionBinder CreateBinder(SearchParserTestContext context)
    {
        return new SearchExpressionBinder(
            new SearchAtomicValueParser(
                new ReferenceSearchValueParser(context.SchemaProvider),
                context.SchemaProvider));
    }

    private static SearchParameterInfo CreateSingleComponentComposite(
        SearchParserTestContext context,
        SearchParamType componentType)
    {
        var component = new SearchParameterComponentInfo
        {
            ResolvedSearchParameter =
                new SearchParameterInfo("component", "component", componentType),
        };

        return context.Add(
            "Observation",
            "composite",
            SearchParamType.Composite,
            components: [component]);
    }
}
