// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchPredicateExpressionBuilderTests
{
    [Fact]
    public void GivenAStringValue_WhenBuilt_ThenReturnsPredicateCarryingTheSameValue()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String);
        var value = new StringSearchValue("Smith");
        var builder = new SearchPredicateExpressionBuilder();

        // Act
        var predicate = builder.Build(parameter, modifier: null, SearchComparator.Eq, value);

        // Assert
        predicate.Parameter.ShouldBeSameAs(parameter);
        predicate.Comparator.ShouldBe(SearchComparator.Eq);
        predicate.Modifier.ShouldBeNull();
        predicate.Value.ShouldBeSameAs(value);
    }

    [Fact]
    public void GivenAModifierAndComparator_WhenBuilt_ThenBothArePreservedOnThePredicate()
    {
        // Arrange
        var parameter = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date);
        var value = new DateTimeSearchValue(DateTimeOffset.UtcNow);
        var modifier = new SearchModifier(SearchModifierCode.Missing);
        var builder = new SearchPredicateExpressionBuilder();

        // Act
        var predicate = builder.Build(parameter, modifier, SearchComparator.Ge, value);

        // Assert
        predicate.Comparator.ShouldBe(SearchComparator.Ge);
        predicate.Modifier.ShouldBe(modifier);
        predicate.Value.ShouldBeSameAs(value);
    }
}
