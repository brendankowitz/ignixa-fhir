// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Covers <see cref="SearchModifierNotSupportedException.ThrowIfAny"/> directly. Every caller that
/// builds <see cref="SearchOptions"/> from client input is expected to call this immediately -- these
/// tests pin the guard's own behavior so a regression here doesn't hide behind a call site forgetting
/// to invoke it.
/// </summary>
public class SearchModifierNotSupportedExceptionTests
{
    [Fact]
    public void GivenNoUnsupportedModifiers_WhenThrowIfAnyCalled_ThenNothingIsThrown()
    {
        // Arrange
        var options = new SearchOptions();

        // Act, Assert
        Should.NotThrow(() => SearchModifierNotSupportedException.ThrowIfAny(options));
    }

    [Fact]
    public void GivenUnsupportedModifiers_WhenThrowIfAnyCalled_ThenThrowsNamingEveryOne()
    {
        // Arrange
        var options = new SearchOptions
        {
            UnsupportedModifierParams = ["_id:above", "_lastUpdated:above"],
        };

        // Act
        var exception = Should.Throw<SearchModifierNotSupportedException>(
            () => SearchModifierNotSupportedException.ThrowIfAny(options));

        // Assert
        exception.Message.ShouldContain("_id:above");
        exception.Message.ShouldContain("_lastUpdated:above");
    }

    [Fact]
    public void GivenUnsupportedModifiersAndAResourceType_WhenThrowIfAnyCalled_ThenTheMessageNamesTheResourceType()
    {
        // Arrange
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            UnsupportedModifierParams = ["_id:above"],
        };

        // Act
        var exception = Should.Throw<SearchModifierNotSupportedException>(
            () => SearchModifierNotSupportedException.ThrowIfAny(options));

        // Assert
        exception.Message.ShouldContain("Patient");
    }

    [Fact]
    public void GivenNull_WhenThrowIfAnyCalled_ThenArgumentNullExceptionIsThrown()
    {
        // Arrange, Act, Assert
        Should.Throw<ArgumentNullException>(() => SearchModifierNotSupportedException.ThrowIfAny(null!));
    }
}
