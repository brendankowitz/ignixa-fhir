// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Ignixa.Extensions.FirelySdk;
using Xunit;

namespace Ignixa.Extensions.Tests.FirelySdk;

/// <summary>
/// A named lookup through <see cref="IgnixaElementAdapter"/> is an exact-name match. Firely 6 also
/// answers <c>Children("valueDateTime")</c> with the <c>value</c> child, which this adapter has never
/// done, so it filters what the wrapped element returns. Firely 5.11.4 does not resolve suffixed
/// names, so only a Firely 6 run can pin this.
/// </summary>
public class IgnixaElementAdapterExactNameLookupTests
{
    private const string ObservationJson = """
    {
      "resourceType": "Observation",
      "status": "final",
      "code": { "text": "t" },
      "valueDateTime": "2025-01-01T12:00:00Z"
    }
    """;

    [Fact]
    public void GivenObservationWithChoiceValue_WhenLookingUpByBaseName_ThenReturnsTheChoiceChild()
    {
        // Arrange
        var adapter = CreateAdapter();

        // Act
        var result = adapter.Children("value");

        // Assert
        var child = Assert.Single(result);
        Assert.Equal("value", child.Name);
        Assert.Equal("dateTime", child.InstanceType);
    }

    [Fact]
    public void GivenObservationWithChoiceValue_WhenLookingUpBySuffixedName_ThenReturnsNothing()
    {
        // Arrange
        var adapter = CreateAdapter();

        // Act
        var result = adapter.Children("valueDateTime");

        // Assert
        Assert.Empty(result);
    }

    private static IgnixaElementAdapter CreateAdapter() =>
        new(FhirJsonNode.Parse(ObservationJson).ToTypedElement(new PocoStructureDefinitionSummaryProvider()));
}
