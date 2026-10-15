// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using static Ignixa.Search.Tests.InMemory.SearchCorpus;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// Guards: behavior the per-entry interpreter must keep from the one before it. Each row passes at 419b668 and was
/// mutation-checked (the behavior it pins was broken, the row failed, the break was reverted); the regressions that
/// changed live in <see cref="SearchQueryInterpreterMatchingTests"/>.
/// </summary>
public class SearchQueryInterpreterMatchingGuardTests
{
    [Fact]
    public void GivenCodingsFromDifferentSystems_WhenSearchedBySystemAndCodeOfOneCoding_ThenThatResourceMatches()
    {
        Match(
            "Observation",
            "code=http://snomed.info/sct|8867-4",
            """{"resourceType":"Observation","id":"mixed","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"1234-5"},{"system":"http://snomed.info/sct","code":"8867-4"}]}}""",
            """{"resourceType":"Observation","id":"hr","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]}}""")
            .ShouldBe(["mixed"]);
    }

    [Theory]
    [InlineData("date=sa2020-12-31T23:59:00Z", true)]
    [InlineData("date=sa2021-01-01T00:10:00Z", false)]
    [InlineData("date=gt2021-01-01T01:00:00Z", false)]
    [InlineData("date=lt2021-01-01T00:15:00Z", true)]
    public void GivenAPeriod_WhenSearchedWithAPrefixOnItsStartOrBeyondItsEnd_ThenTheResultIsUnchanged(string query, bool matches)
    {
        string[] matched = Match(
            "Encounter",
            query,
            """{"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"period":{"start":"2021-01-01T00:00:00Z","end":"2021-01-01T00:30:00Z"}}""");

        matched.ShouldBe(matches ? ["e1"] : []);
    }

    [Theory]
    [InlineData("probability=gt0.3")]
    [InlineData("probability=eb0.6")]
    public void GivenARange_WhenSearchedWithAPrefixOnItsHighBound_ThenItMatches(string query)
    {
        Definitions.TryGetSearchParameter("RiskAssessment", "probability", out SearchParameterInfo probability).ShouldBeTrue();
        (ResourceKey, IReadOnlyCollection<SearchIndexEntry>) range =
            (new ResourceKey("RiskAssessment", "r1"), [new SearchIndexEntry(probability, new NumberSearchValue(0.2m, 0.5m))]);

        Match("RiskAssessment", query, [range]).ShouldBe(["r1"]);
    }

    [Theory]
    [InlineData("family=Ödegaard")]
    [InlineData("family:contains=DEGA")]
    public void GivenAccentedName_WhenSearchedWithTheAccentOrCaseInsensitively_ThenItMatches(string query)
    {
        Match("Patient", query, """{"resourceType":"Patient","id":"p3","name":[{"family":"Ödegaard"}]}""").ShouldBe(["p3"]);
    }

    [Fact]
    public void GivenAnIdNoResourceHas_WhenSearchedById_ThenNothingMatches()
    {
        Match("Patient", "_id=nope", """{"resourceType":"Patient","id":"p1"}""", """{"resourceType":"Patient","id":"p2"}""").ShouldBeEmpty();
    }
}
