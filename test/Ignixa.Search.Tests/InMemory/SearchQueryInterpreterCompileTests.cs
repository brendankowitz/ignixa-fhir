// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using static Ignixa.Search.Tests.InMemory.SearchCorpus;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// What the interpreter rejects when it compiles a query, and what it leaves to each index entry: a query shape it
/// cannot evaluate fails before any entry is read, while an entry of an unexpected kind just does not match.
/// </summary>
public class SearchQueryInterpreterCompileTests
{
    // A composite component is typed by the indexer from the element it reads, so one Group's value component holds a
    // Quantity where the definition (and the query) expects a token.
    private const string GenderGroup = """{"resourceType":"Group","id":"g1","type":"person","actual":true,"characteristic":[{"code":{"coding":[{"system":"http://s","code":"gender"}]},"valueCodeableConcept":{"coding":[{"code":"male"}]},"exclude":false}]}""";

    private const string AgeGroup = """{"resourceType":"Group","id":"g2","type":"person","actual":true,"characteristic":[{"code":{"coding":[{"system":"http://s","code":"age"}]},"valueQuantity":{"value":40,"unit":"a","system":"http://unitsofmeasure.org","code":"a"},"exclude":false}]}""";

    private const string FlagGroup = """{"resourceType":"Group","id":"g3","type":"person","actual":true,"characteristic":[{"code":{"coding":[{"system":"http://s","code":"flag"}]},"valueBoolean":true,"exclude":false}]}""";

    [Theory]
    [InlineData("characteristic-value=age$40", "")]
    [InlineData("characteristic-value=gender$male", "g1")]
    [InlineData("characteristic-value=flag$true", "g3")]
    [InlineData("characteristic-value=http://s|age$40", "")]
    public void GivenCompositeComponentsOfDifferentKinds_WhenSearched_ThenAnEntryOfAnotherKindDoesNotMatchInsteadOfFailingTheSearch(string query, string expected)
    {
        Match("Group", query, GenderGroup, AgeGroup, FlagGroup).ShouldBe(expected.Length == 0 ? [] : expected.Split(','));
    }

    [Fact]
    public void GivenAFieldTheParameterCanNeverHold_WhenCompiled_ThenTheQueryIsRejectedBeforeAnyEntryIsRead()
    {
        Definitions.TryGetSearchParameter("Patient", "gender", out SearchParameterInfo gender).ShouldBeTrue();
        Expression query = Expression.SearchParameter(gender, Expression.GreaterThan(FieldName.NumberLow, null, 5m));
        var interpreter = new SearchQueryInterpreter();

        Should.Throw<SearchOperationNotSupportedException>(() => query.AcceptVisitor(interpreter, interpreter.InitialContext));
    }

    [Theory]
    [InlineData(FieldName.DateTimeStart)]
    [InlineData(FieldName.ParamName)]
    public void GivenAStringComparisonOnAFieldThatIsNotAString_WhenCompiled_ThenTheQueryIsRejected(FieldName field)
    {
        Definitions.TryGetSearchParameter("Patient", "birthdate", out SearchParameterInfo birthdate).ShouldBeTrue();
        Expression query = Expression.SearchParameter(birthdate, Expression.StartsWith(field, null, "1980", ignoreCase: true));
        var interpreter = new SearchQueryInterpreter();

        Should.Throw<SearchOperationNotSupportedException>(() => query.AcceptVisitor(interpreter, interpreter.InitialContext));
    }

    [Fact]
    public void GivenACompositeComponentBeyondTheDefinition_WhenCompiled_ThenTheQueryIsRejected()
    {
        Definitions.TryGetSearchParameter("Group", "characteristic-value", out SearchParameterInfo composite).ShouldBeTrue();
        Expression query = Expression.SearchParameter(composite, Expression.StringEquals(FieldName.TokenCode, 5, "x", false));
        var interpreter = new SearchQueryInterpreter();

        Should.Throw<SearchOperationNotSupportedException>(() => query.AcceptVisitor(interpreter, interpreter.InitialContext));
    }

    [Theory]
    [InlineData("_id=|p1")]
    [InlineData("_id=http://s|p1")]
    public void GivenAnIdWithASystem_WhenCompiled_ThenTheQueryIsRejectedAsABadRequest(string query)
    {
        Should.Throw<BadSearchRequestException>(() => Match("Patient", query, """{"resourceType":"Patient","id":"p1"}"""));
    }

    [Fact]
    public void GivenOddValuesInTheIndex_WhenAnyParameterIsSearched_ThenNoEntryFailsTheSearch()
    {
        // Every kind of index value, all filed under one token parameter, searched with each kind of token criterion.
        Definitions.TryGetSearchParameter("Observation", "code", out SearchParameterInfo code).ShouldBeTrue();
        var odd = (new ResourceKey("Observation", "odd"), (IReadOnlyCollection<SearchIndexEntry>)
        [
            new SearchIndexEntry(code, new QuantitySearchValue("http://u", "kg", 1m)),
            new SearchIndexEntry(code, new StringSearchValue("x")),
            new SearchIndexEntry(code, DateTimeSearchValue.Parse("2020-01-01")),
            new SearchIndexEntry(code, new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, "Patient", "p1")),
        ]);

        foreach (string query in new[] { "code=http://loinc.org|1", "code=|1", "code:text=x", "code:not=1", "code=1" })
        {
            Match("Observation", query, [odd]).ShouldBe(query.StartsWith("code:not", StringComparison.Ordinal) ? ["odd"] : [], query);
        }
    }
}
