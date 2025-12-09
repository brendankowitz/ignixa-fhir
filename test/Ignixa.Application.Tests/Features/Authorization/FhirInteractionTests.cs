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
    [InlineData(FhirInteraction.Read, "read")]
    [InlineData(FhirInteraction.VRead, "read")]
    [InlineData(FhirInteraction.SearchType, "read")]
    [InlineData(FhirInteraction.SearchSystem, "read")]
    [InlineData(FhirInteraction.Create, "create")]
    [InlineData(FhirInteraction.Update, "update")]
    [InlineData(FhirInteraction.Patch, "update")]
    [InlineData(FhirInteraction.Delete, "delete")]
    public void ToSmartPermission_MapsCorrectly(FhirInteraction interaction, string expectedPermission)
    {
        // Act
        var result = interaction.ToSmartPermission();

        // Assert
        result.Should().Be(expectedPermission);
    }
}
