// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Application.Tests.Search.Expressions.Parsers;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Legacy;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Expressions;

/// <summary>
/// Proves <see cref="LegacyExpressionLowerer"/> correct: for a representative corpus of query
/// strings, parses each with the frozen <see cref="LegacyExpressionParser"/> (old, untyped shape)
/// and with the live <see cref="ExpressionParser"/> (new, typed-predicate shape), lowers the new
/// tree back through <see cref="LegacyExpressionLowerer"/>, and asserts the two are
/// <see cref="Expression.ValueInsensitiveEquals"/>. This is the same oracle
/// <see cref="Ignixa.Application.Tests.Search.Expressions.Parsers.SearchParserOldVsNewParityTests"/>
/// already established as trustworthy, applied here to prove the lowerer itself.
/// See docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md.
/// </summary>
public class LegacyExpressionLowererParityTests
{
    private static SearchParserTestContext BuildContext()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        context.Add("Patient", "birthdate", SearchParamType.Date);

        SearchParameterInfo codeParam = context.Add("Observation", "component-code", SearchParamType.Token);
        SearchParameterInfo valueParam = context.Add("Observation", "component-value-quantity", SearchParamType.Quantity);
        context.Add(
            "Observation",
            "component-code-value-quantity",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo(codeParam.Url) { ResolvedSearchParameter = codeParam },
                new SearchParameterComponentInfo(valueParam.Url) { ResolvedSearchParameter = valueParam }
            ]);

        return context;
    }

    private static IExpressionParser BuildOldParser(SearchParserTestContext context)
    {
        var legacyValueParser = new LegacySearchParameterExpressionParser(
            new ReferenceSearchValueParser(context.SchemaProvider),
            context.SchemaProvider);

        return new LegacyExpressionParser(() => context.DefinitionManager, legacyValueParser, context.SchemaProvider);
    }

    private static (string Key, string Value) SplitQuery(string query)
    {
        int index = query.IndexOf('=');
        return (query[..index], query[(index + 1)..]);
    }

    [Theory]
    [InlineData("Patient", "name=Smith")]
    [InlineData("Patient", "name:exact=Smith")]
    [InlineData("Patient", "birthdate=ge2020-01-01")]
    [InlineData("Observation", "component-code-value-quantity=http://loinc.org|1234-5$gt10")]
    public void GivenAQueryString_WhenParsedBothWays_ThenLoweredNewTreeMatchesLegacyTree(string resourceType, string query)
    {
        // Arrange
        var context = BuildContext();
        var resourceTypes = new[] { resourceType };
        (string key, string value) = SplitQuery(query);

        IExpressionParser oldParser = BuildOldParser(context);
        Expression legacyExpression = oldParser.Parse(resourceTypes, key, value);
        Expression newExpression = context.Parser.Parse(resourceTypes, key, value);
        var lowerer = new LegacyExpressionLowerer();

        // Act
        Expression lowered = newExpression.AcceptVisitor(lowerer, context: null);

        // Assert
        lowered.ValueInsensitiveEquals(legacyExpression).ShouldBeTrue($"Legacy: {legacyExpression}\nLowered: {lowered}");
    }
}
