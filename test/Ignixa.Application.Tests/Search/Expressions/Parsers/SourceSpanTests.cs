// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SourceSpanTests
{
    [Fact]
    public void GivenAScalarValue_WhenScanned_ThenTheSpanExtractsTheWholeToken()
    {
        const string source = "Smith";

        var syntax = (AtomicValueSyntax)SearchValueSyntaxParser.Parse(
            SearchParamType.String, modifier: null, source);

        source.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("Smith");
        syntax.Span.Origin.ShouldBe(SourceOrigin.Value);
    }

    [Fact]
    public void GivenAComparatorPrefixedValue_WhenScanned_ThenTheSpanIncludesThePrefix()
    {
        const string source = "gt2000";

        var syntax = (AtomicValueSyntax)SearchValueSyntaxParser.Parse(
            SearchParamType.Date, modifier: null, source);

        syntax.RawText.ShouldBe("2000");
        source.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("gt2000");
    }

    [Fact]
    public void GivenCommaAlternatives_WhenScanned_ThenEachItemSpanExtractsItsOwnText()
    {
        const string source = "alpha,beta";

        var syntax = (AlternativesValueSyntax)SearchValueSyntaxParser.Parse(
            SearchParamType.String, modifier: null, source);

        source.Substring(syntax.Items[0].Span.Start, syntax.Items[0].Span.Length).ShouldBe("alpha");
        source.Substring(syntax.Items[1].Span.Start, syntax.Items[1].Span.Length).ShouldBe("beta");
    }

    [Fact]
    public void GivenTwoValuesDifferingOnlyBySpan_WhenCompared_ThenTheyAreEqual()
    {
        var a = new AtomicValueSyntax("x", SearchComparator.Eq) { Span = new SourceSpan(SourceOrigin.Value, 0, 1) };
        var b = new AtomicValueSyntax("x", SearchComparator.Eq) { Span = new SourceSpan(SourceOrigin.Value, 5, 1) };

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void GivenAModifiedKey_WhenScanned_ThenTheSpanCoversTheWholeKey()
    {
        const string key = "name:exact";

        var syntax = SearchKeySyntaxParser.ParseParameter(key);

        key.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("name:exact");
        syntax.Span.Origin.ShouldBe(SourceOrigin.Key);
    }

    [Fact]
    public void GivenAForwardChainKey_WhenScanned_ThenTheChainSpanCoversTheWholeKey()
    {
        const string key = "general-practitioner.name";

        var syntax = (ForwardChainKeySyntax)SearchKeySyntaxParser.ParseParameter(key);

        key.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("general-practitioner.name");
        key.Substring(syntax.Next.Span.Start, syntax.Next.Span.Length).ShouldBe("name");
    }

    [Fact]
    public void GivenAReverseChainKey_WhenScanned_ThenTheChainSpanCoversTheWholeKey()
    {
        const string key = "_has:Observation:patient:code";

        var syntax = (ReverseChainKeySyntax)SearchKeySyntaxParser.ParseParameter(key);

        key.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("_has:Observation:patient:code");
        key.Substring(syntax.Next.Span.Start, syntax.Next.Span.Length).ShouldBe("code");
    }
}
