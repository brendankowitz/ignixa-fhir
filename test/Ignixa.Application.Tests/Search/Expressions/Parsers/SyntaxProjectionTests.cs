// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SyntaxProjectionTests
{
    private static readonly string[] Patient = ["Patient"];

    [Fact]
    public void GivenAlternatives_WhenParsedWithSyntax_ThenEachChildSpanExtractsItsText()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        const string value = "alpha,beta";

        var result = context.Parser.ParseWithSyntax(Patient, "name", value);

        result.ValueSyntax.ShouldNotBeNull();
        result.ValueSyntax!.Kind.ShouldBe("Alternatives");
        result.ValueSyntax.Children.Count.ShouldBe(2);
        value.Substring(result.ValueSyntax.Children[0].Span.Start, result.ValueSyntax.Children[0].Span.Length)
            .ShouldBe("alpha");
        value.Substring(result.ValueSyntax.Children[1].Span.Start, result.ValueSyntax.Children[1].Span.Length)
            .ShouldBe("beta");
    }

    [Fact]
    public void GivenAnOrdinaryParameter_WhenParsedWithSyntax_ThenTheExpressionMatchesPlainParse()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        var plain = context.Parser.Parse(Patient, "name", "Smith");
        var withSyntax = context.Parser.ParseWithSyntax(Patient, "name", "Smith");

        withSyntax.Expression.ToString().ShouldBe(plain.ToString());
    }
}
