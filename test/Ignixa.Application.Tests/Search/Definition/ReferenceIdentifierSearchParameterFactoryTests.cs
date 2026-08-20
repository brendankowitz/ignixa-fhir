// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Definition;

public class ReferenceIdentifierSearchParameterFactoryTests
{
    private readonly SearchParameterInfo _referenceParameter = new(
        name: "subject",
        code: "subject",
        searchParamType: SearchParamType.Reference,
        url: new Uri("http://hl7.org/fhir/SearchParameter/Encounter-subject"),
        expression: "Encounter.subject",
        targetResourceTypes: ["Group", "Patient"],
        baseResourceTypes: ["Encounter"]);

    [Fact]
    public void GivenReferenceParameter_WhenDerivingIdentity_ThenUrlAndCodeMatchContract()
    {
        Uri url = ReferenceIdentifierSearchParameterFactory.DeriveUrl(_referenceParameter);
        string code = ReferenceIdentifierSearchParameterFactory.DeriveCode(_referenceParameter.Code);

        url.OriginalString.ShouldBe("http://hl7.org/fhir/SearchParameter/Encounter-subject#identifier");
        code.ShouldBe("subject:identifier");
    }

    [Fact]
    public void GivenReferenceParameter_WhenCreatingDerivedParameter_ThenContractIsPreserved()
    {
        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(_referenceParameter);

        derived.Name.ShouldBe("subject:identifier");
        derived.Code.ShouldBe("subject:identifier");
        derived.Url.OriginalString.ShouldBe("http://hl7.org/fhir/SearchParameter/Encounter-subject#identifier");
        derived.Type.ShouldBe(SearchParamType.Token);
        derived.Expression.ShouldBe(_referenceParameter.Expression);
        derived.BaseResourceTypes.ShouldBe(_referenceParameter.BaseResourceTypes);
        derived.TargetResourceTypes.ShouldBeEmpty();
        derived.IsSupported.ShouldBeTrue();
        derived.IsSearchable.ShouldBeTrue();
        derived.IsDerived.ShouldBeTrue();
        derived.ShouldNotBe(_referenceParameter);
    }

    [Fact]
    public void GivenOriginalAndDerivedUrls_WhenComparingIdentity_ThenFragmentsRemainDistinct()
    {
        Uri derivedUrl = ReferenceIdentifierSearchParameterFactory.DeriveUrl(_referenceParameter);

        SearchParameterUriComparer.Instance.Equals(_referenceParameter.Url, derivedUrl).ShouldBeFalse();
        SearchParameterUriComparer.Instance.GetHashCode(_referenceParameter.Url)
            .ShouldNotBe(SearchParameterUriComparer.Instance.GetHashCode(derivedUrl));
    }
}
