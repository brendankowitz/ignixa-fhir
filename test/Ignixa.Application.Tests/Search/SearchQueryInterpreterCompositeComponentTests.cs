// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

public class SearchQueryInterpreterCompositeComponentTests
{
    private static readonly SearchParameterInfo TokenComponentParam = new("code", "code", SearchParamType.Token);

    [Fact]
    public void GivenComponent_WhenVisited_ThenPredicateComesFromWrappedExpression()
    {
        var interpreter = new SearchQueryInterpreter();
        var context = interpreter.InitialContext.WithParameterName("code");
        var wrapped = Expression.StringEquals(FieldName.TokenCode, null, "8480-6", false);
        var direct = wrapped.AcceptVisitor(interpreter, context);

        var component = new CompositeComponentExpression(TokenComponentParam, 0, wrapped);
        var viaWrapper = interpreter.VisitCompositeComponent(component, context);

        // Both predicates are functionally equivalent - built from the identical wrapped expression
        // via the identical visitor and context, so they must have the same delegate target method.
        direct.Method.ShouldBe(viaWrapper.Method);
    }
}
