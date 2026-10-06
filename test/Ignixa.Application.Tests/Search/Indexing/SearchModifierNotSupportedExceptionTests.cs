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

    [Fact]
    public void GivenUnsupportedModifierWithReason_WhenThrowIfAnyCalled_ThenMessageIncludesReasonNotGenericText()
    {
        // Arrange: a semantic search chain rejection has a specific reason (the chain terminates in
        // a semantic parameter), and a reason is recorded. The message should use that reason instead
        // of the generic "uses a modifier that is not supported" text, which would misreport the actual
        // issue.
        var options = new SearchOptions
        {
            UnsupportedModifierParams = ["subject:Patient.semantic-text"],
            UnsupportedModifierReasons = new Dictionary<string, string>
            {
                ["subject:Patient.semantic-text"] = "Chains cannot terminate in a semantic search parameter"
            }
        };

        // Act
        var exception = Should.Throw<SearchModifierNotSupportedException>(
            () => SearchModifierNotSupportedException.ThrowIfAny(options));

        // Assert: the specific reason is in the message, not the generic text
        exception.Message.ShouldContain("subject:Patient.semantic-text");
        exception.Message.ShouldContain("Chains cannot terminate in a semantic search parameter");
        exception.Message.ShouldNotContain("uses a modifier that is not supported");
    }

    [Fact]
    public void GivenMixedModifiersWithAndWithoutReasons_WhenThrowIfAnyCalled_ThenMessageRendersEachAppropriately()
    {
        // Arrange: one parameter has a specific reason, another is a genuine unsupported modifier.
        var options = new SearchOptions
        {
            UnsupportedModifierParams = ["subject:Patient.semantic-text", "_id:above"],
            UnsupportedModifierReasons = new Dictionary<string, string>
            {
                ["subject:Patient.semantic-text"] = "Chains cannot terminate in a semantic search parameter"
            }
        };

        // Act
        var exception = Should.Throw<SearchModifierNotSupportedException>(
            () => SearchModifierNotSupportedException.ThrowIfAny(options));

        // Assert: the parameter with a reason shows the reason, the one without keeps old format
        exception.Message.ShouldContain("subject:Patient.semantic-text': Chains cannot terminate in a semantic search parameter");
        exception.Message.ShouldContain("_id:above");
    }
}
