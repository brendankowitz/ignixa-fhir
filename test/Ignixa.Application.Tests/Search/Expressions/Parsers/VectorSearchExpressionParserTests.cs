// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

/// <summary>
/// Task 2 of the slice-1 plan: <see cref="VectorSearchExpression"/>'s verbatim parsing (no comma
/// splitting, no modifiers, no chains) and the <see cref="IExpressionVisitor{TContext,TOutput}.VisitVectorSearch"/>
/// default-throw contract existing visitors must keep until Task 7 implements SQL lowering.
/// </summary>
public class VectorSearchExpressionParserTests
{
    private static readonly VectorSearchConfig ValidVectorConfig = new(
        VectorTextExtractionPolicy.Concatenate,
        MaxInputTokens: 8000,
        MinimumScore: 0m,
        ChunkSizeTokens: null,
        ChunkOverlapTokens: null);

    private static SearchParameterInfo AddSemantic(SearchParserTestContext context, string resourceType, string code = "semantic-text")
    {
        var parameter = new SearchParameterInfo(
            name: code,
            code: code,
            searchParamType: SearchParamType.Special,
            url: new Uri($"http://ignixa.test/SearchParameter/{resourceType}-{code}"),
            baseResourceTypes: new[] { resourceType },
            vectorConfig: ValidVectorConfig);

        context.AddCommon(parameter, resourceType);
        return parameter;
    }

    [Fact]
    public void GivenSemanticParam_WhenValueContainsComma_ThenSingleExpressionWithVerbatimText()
    {
        var context = new SearchParserTestContext();
        var parameter = AddSemantic(context, "Patient");

        Expression expression = context.Parser.Parse(["Patient"], "semantic-text", "chest pain, nausea");

        var vectorSearch = expression.ShouldBeOfType<VectorSearchExpression>();
        vectorSearch.Parameter.ShouldBeSameAs(parameter);
        vectorSearch.QueryText.ShouldBe("chest pain, nausea");
        vectorSearch.Prepared.ShouldBeNull();
    }

    [Fact]
    public void GivenSemanticParam_WhenValueHasOuterWhitespace_ThenTrimmedOnlyAtTheEnds()
    {
        var context = new SearchParserTestContext();
        AddSemantic(context, "Patient");

        Expression expression = context.Parser.Parse(["Patient"], "semantic-text", "  chest  pain  ");

        var vectorSearch = expression.ShouldBeOfType<VectorSearchExpression>();
        vectorSearch.QueryText.ShouldBe("chest  pain");
    }

    [Fact]
    public void GivenSemanticParam_WhenValueIsSingleNonWhitespaceCharacter_ThenNotRejectedAsEmpty()
    {
        // ParseCore's own EnsureArg.IsNotNullOrWhiteSpace guard (shared by every parameter type) already
        // guarantees a non-blank value reaches the semantic branch, so trimming can never produce an
        // empty query here -- this pins that no redundant/dead empty check exists downstream.
        var context = new SearchParserTestContext();
        var parameter = AddSemantic(context, "Patient");

        Expression expression = context.ValueParser.Parse(parameter, null, " x ");

        expression.ShouldBeOfType<VectorSearchExpression>().QueryText.ShouldBe("x");
    }

    [Theory]
    [InlineData(SearchModifierCode.Missing)]
    [InlineData(SearchModifierCode.Text)]
    [InlineData(SearchModifierCode.Exact)]
    public void GivenSemanticParam_WhenModifierSupplied_ThenSearchModifierNotSupportedException(SearchModifierCode modifierCode)
    {
        var context = new SearchParserTestContext();
        var parameter = AddSemantic(context, "Patient");
        var modifier = new SearchModifier(modifierCode);

        // Must be the SearchModifierNotSupportedException subclass, not merely the
        // InvalidSearchOperationException base: SearchOptionsBuilder's catch order routes only the
        // subclass into SearchOptions.UnsupportedModifierParams, which FhirEndpoints.CheckStrictHandling
        // rejects with a 400 by default (no Prefer header needed) per R4's modifier SHALL. See the
        // SearchOptionsBuilder-level regression in SemanticSearchModifierHandlingTests for the
        // end-to-end assertion of that routing.
        Should.Throw<SearchModifierNotSupportedException>(() => context.ValueParser.Parse(parameter, modifier, "chest pain"));
    }

    [Fact]
    public void GivenSemanticParam_WhenChained_ThenSearchModifierNotSupportedException()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: ["Patient"]);
        AddSemantic(context, "Patient");

        // Same derived-exception requirement as the modifier case above: a chain into a semantic
        // parameter must default-reject with a 400, not fall into the silently-ignored
        // UnsupportedParams list.
        var exception = Should.Throw<SearchModifierNotSupportedException>(
            () => context.Parser.Parse(["Observation"], "subject:Patient.semantic-text", "chest pain"));

        exception.Message.ShouldBe(Resources.SemanticSearchChainNotSupported);
    }

    [Fact]
    public void GivenSemanticParam_WhenReverseChained_ThenSearchModifierNotSupportedException()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: ["Patient"]);
        // _has:Observation:subject:semantic-text resolves "semantic-text" against Observation (the
        // resource that HAS the reference), not Patient.
        AddSemantic(context, "Observation");

        var exception = Should.Throw<SearchModifierNotSupportedException>(
            () => context.Parser.Parse(["Patient"], "_has:Observation:subject:semantic-text", "chest pain"));

        exception.Message.ShouldBe(Resources.SemanticSearchChainNotSupported);
    }

    [Fact]
    public void GivenVectorSearchExpression_WhenWithPrepared_ThenReturnsNewInstanceCarryingIt()
    {
        var context = new SearchParserTestContext();
        var parameter = AddSemantic(context, "Patient");
        var original = new VectorSearchExpression(parameter, "chest pain");
        var prepared = new PreparedVectorQuery(new float[1536], "text-embedding-3-small|1", 1.0);

        var withPrepared = original.WithPrepared(prepared);

        withPrepared.ShouldNotBeSameAs(original);
        withPrepared.Prepared.ShouldBeSameAs(prepared);
        original.Prepared.ShouldBeNull();
        withPrepared.Parameter.ShouldBeSameAs(parameter);
        withPrepared.QueryText.ShouldBe("chest pain");
    }

    [Fact]
    public void GivenTwoVectorSearchExpressionsWithDifferentQueryText_WhenValueInsensitiveEquals_ThenStillEqual()
    {
        var context = new SearchParserTestContext();
        var parameter = AddSemantic(context, "Patient");
        var first = new VectorSearchExpression(parameter, "chest pain");
        var second = new VectorSearchExpression(parameter, "a completely different query");

        // QueryText is the parameterizable value (like StringExpression.Value) and is deliberately
        // excluded from value-insensitive comparison, matching Expression's documented contract.
        first.ValueInsensitiveEquals(second).ShouldBeTrue();
    }

    [Fact]
    public void GivenVectorSearchExpression_WhenVisitedByDefaultVisitor_ThenNotSupportedException()
    {
        var context = new SearchParserTestContext();
        var parameter = AddSemantic(context, "Patient");
        var expression = new VectorSearchExpression(parameter, "chest pain");

        // SearchQueryInterpreter implements IExpressionVisitor directly, without overriding
        // VisitVectorSearch, so this exercises IExpressionVisitor<,>.VisitVectorSearch's default-interface
        // throw -- the in-memory backend keeps this default for now (SQL lowering is Task 7).
        var interpreter = new SearchQueryInterpreter();

        Should.Throw<NotSupportedException>(() => expression.AcceptVisitor(interpreter, default(SearchQueryInterpreter.Context)));
    }
}
