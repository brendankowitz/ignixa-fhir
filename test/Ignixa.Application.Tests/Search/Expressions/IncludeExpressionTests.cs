// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search.Expressions;

/// <summary>Spec F1, §4.2: a wildcard include is identified by the searchable reference parameters it resolved to.</summary>
public class IncludeExpressionTests
{
    private static readonly SearchParameterInfo Subject = new("subject", "subject", SearchParamType.Reference);
    private static readonly SearchParameterInfo Performer = new("performer", "performer", SearchParamType.Reference);

    [Fact]
    public void GivenWildcardIncludes_WhenComparingValueInsensitively_ThenTheResolvedParametersDecideEquality()
    {
        var both = Wildcard(Subject, Performer);

        both.ValueInsensitiveEquals(Wildcard(Subject, Performer)).ShouldBeTrue();
        both.ValueInsensitiveEquals(Wildcard(Subject)).ShouldBeFalse();
        both.ValueInsensitiveEquals(Wildcard(Performer, Subject)).ShouldBeFalse();
        HashOf(both).ShouldBe(HashOf(Wildcard(Subject, Performer)));
    }

    private static IncludeExpression Wildcard(params SearchParameterInfo[] resolved) =>
        new(["Observation"], null!, "Observation", null!, null, wildCard: true, reversed: false, iterate: false, resolved);

    private static int HashOf(Expression expression)
    {
        var hashCode = new HashCode();
        expression.AddValueInsensitiveHashCode(ref hashCode);
        return hashCode.ToHashCode();
    }
}
