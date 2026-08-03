// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa. All rights reserved.
// Licensed under the MIT License (MIT).
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Models;

public class SearchParameterInfoEqualityTests
{
    private static readonly Uri CanonicalUrl = new("http://hl7.org/fhir/SearchParameter/Resource-type");

    [Fact]
    public void GivenTwoParametersSharingAUrlButDifferingElsewhere_WhenCompared_ThenTheyAreEqualAndHashAlike()
    {
        // Equals decides on Url alone, so the hash must too. When it did not, a HashSet kept both entries and
        // the per-resource rebuild in SearchParameterDefinitionBuilder threw on the resulting duplicate code.
        var first = new SearchParameterInfo("_type", "_type", SearchParamType.Token, CanonicalUrl);
        var second = new SearchParameterInfo("_type", "_type", SearchParamType.Token, CanonicalUrl, expression: "Resource.type().name");

        first.Equals(second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
        new HashSet<SearchParameterInfo> { first, second }.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenTwoParametersSharingAUrlButDifferingByCode_WhenCompared_ThenTheCodeIsNotConsulted()
    {
        // Intended semantics, not an accident: the canonical URL wins outright, so a set built from both
        // keeps whichever arrived first and the other Code disappears without a diagnostic. Two definitions
        // carrying the same canonical URL under different codes are a definition-authoring error, and this
        // is where the collapse happens.
        var first = new SearchParameterInfo("_type", "_type", SearchParamType.Token, CanonicalUrl);
        var second = new SearchParameterInfo("resource-type", "resource-type", SearchParamType.Token, CanonicalUrl);

        first.Equals(second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());

        var set = new HashSet<SearchParameterInfo> { first, second };
        set.Count.ShouldBe(1);
        set.Single().Code.ShouldBe("_type");
    }

    [Fact]
    public void GivenOneParameterWithAUrlAndOneWithout_WhenCompared_ThenTheyAreNotEqual()
    {
        // The boundary between the two GetHashCode branches: one side hashes on Url, the other on
        // Code/Type/Expression. Equals must reject the pair in both directions or the branches disagree.
        var withUrl = new SearchParameterInfo("_type", "_type", SearchParamType.Token, CanonicalUrl, expression: "Patient.name");
        var withoutUrl = new SearchParameterInfo("_type", "_type", SearchParamType.Token, url: null, expression: "Patient.name");

        withUrl.Equals(withoutUrl).ShouldBeFalse();
        withoutUrl.Equals(withUrl).ShouldBeFalse();
        new HashSet<SearchParameterInfo> { withUrl, withoutUrl }.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenTwoParametersWithDifferentUrls_WhenCompared_ThenTheyAreNotEqual()
    {
        var baseParameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var custom = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://example.org/fhir/SearchParameter/custom-name"));

        baseParameter.Equals(custom).ShouldBeFalse();
        new HashSet<SearchParameterInfo> { baseParameter, custom }.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenTwoUrllessParametersMatchingOnCodeTypeAndExpression_WhenCompared_ThenTheyAreEqualAndHashAlike()
    {
        // The fallback branch: with no canonical URL there is nothing else to identify the parameter by, so
        // Equals compares the three descriptive fields and GetHashCode must combine the same three.
        var first = new SearchParameterInfo("adhoc", "adhoc", SearchParamType.String, url: null, expression: "Patient.name");
        var second = new SearchParameterInfo("adhoc", "adhoc", SearchParamType.String, url: null, expression: "Patient.name");

        first.Equals(second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
        new HashSet<SearchParameterInfo> { first, second }.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenTwoUrllessParametersDifferingByExpression_WhenCompared_ThenTheyAreNotEqual()
    {
        var first = new SearchParameterInfo("adhoc", "adhoc", SearchParamType.String, url: null, expression: "Patient.name");
        var second = new SearchParameterInfo("adhoc", "adhoc", SearchParamType.String, url: null, expression: "Patient.birthDate");

        first.Equals(second).ShouldBeFalse();
        new HashSet<SearchParameterInfo> { first, second }.Count.ShouldBe(2);
    }
}
