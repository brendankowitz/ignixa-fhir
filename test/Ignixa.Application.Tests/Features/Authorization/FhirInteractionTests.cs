// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Models;

namespace Ignixa.Application.Tests.Features.Authorization;

public class FhirInteractionTests
{
    [Theory]
    [InlineData(FhirInteraction.Read, "read")]
    [InlineData(FhirInteraction.VRead, "vread")]
    [InlineData(FhirInteraction.Update, "update")]
    [InlineData(FhirInteraction.Patch, "patch")]
    [InlineData(FhirInteraction.Delete, "delete")]
    [InlineData(FhirInteraction.Create, "create")]
    [InlineData(FhirInteraction.SearchType, "search-type")]
    [InlineData(FhirInteraction.HistoryInstance, "history-instance")]
    [InlineData(FhirInteraction.HistoryType, "history-type")]
    public void ToFhirCode_ReturnsCorrectCode(FhirInteraction interaction, string expectedCode)
    {
        // Act
        var result = interaction.ToFhirCode();

        // Assert
        result.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("GET", true, false, false, false, FhirInteraction.Read)]
    [InlineData("GET", false, false, false, false, FhirInteraction.SearchType)]
    [InlineData("POST", false, false, false, false, FhirInteraction.Create)]
    [InlineData("PUT", true, false, false, false, FhirInteraction.Update)]
    [InlineData("DELETE", true, false, false, false, FhirInteraction.Delete)]
    [InlineData("PATCH", true, false, false, false, FhirInteraction.Patch)]
    [InlineData("GET", true, false, true, false, FhirInteraction.HistoryInstance)]
    [InlineData("GET", false, false, true, false, FhirInteraction.HistoryType)]
    public void FromHttpRequest_MapsCorrectly(
        string httpMethod,
        bool hasResourceId,
        bool isSearchEndpoint,
        bool isHistoryEndpoint,
        bool isOperationEndpoint,
        FhirInteraction expectedInteraction)
    {
        // Act
        var result = FhirInteractionExtensions.FromHttpRequest(
            httpMethod, hasResourceId, isSearchEndpoint, isHistoryEndpoint, isOperationEndpoint);

        // Assert
        result.Should().Be(expectedInteraction);
    }
}
