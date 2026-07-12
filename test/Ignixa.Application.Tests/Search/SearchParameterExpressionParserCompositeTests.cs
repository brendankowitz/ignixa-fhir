// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using NSubstitute;
using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

public class SearchParameterExpressionParserCompositeTests
{
    private readonly SearchParameterExpressionParser _parser = new(
        CreateReferenceSearchValueParser(),
        Substitute.For<IFhirSchemaProvider>());

    private static IReferenceSearchValueParser CreateReferenceSearchValueParser()
    {
        // The real ReferenceSearchValueParser needs a populated IFhirSchemaProvider to validate
        // resource types, which is unrelated to what this test exercises (composite wrapping
        // structure, not reference-value parsing correctness). Stub the parser directly instead.
        var referenceSearchValueParser = Substitute.For<IReferenceSearchValueParser>();
        referenceSearchValueParser.Parse(Arg.Any<string>()).Returns(callInfo =>
        {
            string[] parts = callInfo.Arg<string>().Split('/');
            return new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, parts[0], parts[1]);
        });

        return referenceSearchValueParser;
    }

    private static SearchParameterInfo CreateReferenceTokenComposite()
    {
        var referenceComponentDefinition = new SearchParameterInfo("relationship-target", "relationship-target", SearchParamType.Reference);
        var codeComponentDefinition = new SearchParameterInfo("relationship-type", "relationship-type", SearchParamType.Token);

        return new SearchParameterInfo(
            "relationship",
            "relationship",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = referenceComponentDefinition },
                new SearchParameterComponentInfo { ResolvedSearchParameter = codeComponentDefinition },
            ]);
    }

    [Fact]
    public void GivenCompositeValue_WhenParsed_ThenEachComponentIsWrappedWithPositionAndEffectiveType()
    {
        var composite = CreateReferenceTokenComposite();

        var result = (SearchParameterExpression)_parser.Parse(composite, modifier: null, "Patient/123$sys|code1");
        var and = (MultiaryExpression)result.Expression;

        and.MultiaryOperation.ShouldBe(MultiaryOperator.And);
        and.Expressions.Count.ShouldBe(2);

        var component0 = (CompositeComponentExpression)and.Expressions[0];
        component0.Position.ShouldBe(0);
        component0.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Reference);

        var component1 = (CompositeComponentExpression)and.Expressions[1];
        component1.Position.ShouldBe(1);
        component1.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Token);
    }

    [Fact]
    public void GivenValueThatDivergesFromStaticDefinition_WhenParsed_ThenEffectiveTypeIsValueInferredNotStatic()
    {
        // Static definitions say [Reference, Token] (position 0 = Reference, position 1 = Token),
        // but position 0's actual value ("sys|code0") looks Token-shaped and position 1's actual
        // value ("Patient/123") looks Reference-shaped - the DocumentReference "relationship" swap.
        var composite = CreateReferenceTokenComposite();

        var result = (SearchParameterExpression)_parser.Parse(composite, modifier: null, "sys|code0$Patient/123");
        var and = (MultiaryExpression)result.Expression;

        var component0 = (CompositeComponentExpression)and.Expressions[0];
        component0.Position.ShouldBe(0);
        component0.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Token);

        var component1 = (CompositeComponentExpression)and.Expressions[1];
        component1.Position.ShouldBe(1);
        component1.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Reference);
    }
}
