// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class SearchParameterFacadeTests
{
    [Fact]
    public void GivenSearchParameter_WhenReadBack_ThenFieldsRoundTrip()
    {
        var searchParameter = new SearchParameter
        {
            Url = "http://example.org/fhir/SearchParameter/test",
            Name = "test-param",
            Code = "test",
            Description = "A test search parameter",
            Status = PublicationStatus.Active,
            Type = SearchParamType.String,
            Expression = "Patient.name",
        };
        searchParameter.Base.Add("Patient");
        searchParameter.Target.Add("Patient");

        searchParameter.Url.ShouldBe("http://example.org/fhir/SearchParameter/test");
        searchParameter.Name.ShouldBe("test-param");
        searchParameter.Code.ShouldBe("test");
        searchParameter.Description.ShouldBe("A test search parameter");
        searchParameter.Status.ShouldBe(PublicationStatus.Active);
        searchParameter.Type.ShouldBe(SearchParamType.String);
        searchParameter.Expression.ShouldBe("Patient.name");
        searchParameter.Base.ShouldBe(["Patient"]);
        searchParameter.Target.ShouldBe(["Patient"]);
    }

    [Fact]
    public void GivenSearchParameterComponent_WhenReadBack_ThenFieldsRoundTrip()
    {
        var component = new SearchParameterComponent
        {
            Definition = "http://example.org/fhir/SearchParameter/other",
            Expression = "Patient.identifier",
        };

        component.Definition.ShouldBe("http://example.org/fhir/SearchParameter/other");
        component.Expression.ShouldBe("Patient.identifier");
    }
}
