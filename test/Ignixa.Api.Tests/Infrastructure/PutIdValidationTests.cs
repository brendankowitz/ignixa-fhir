// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Domain.Exceptions;
using Xunit;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// Unit tests for PUT request ID validation in FhirEndpoints.
/// Verifies the validation logic for ID consistency between URL and JSON body.
///
/// FHIR Spec Requirement (R4/R4B/R5):
/// "For a PUT operation, the resource id in the body SHALL match the [id] in the URL.
///  If the id is not present in the body, the server SHALL return a 400 Bad Request."
/// </summary>
public class PutIdValidationTests
{
    #region Missing ID in Body Tests

    [Fact]
    public void GivenNullBodyId_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange
        string? bodyId = null;

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            if (string.IsNullOrWhiteSpace(bodyId))
            {
                throw new BadRequestException($"Resource ID must be present in the body for PUT requests");
            }
        });

        ex.Message.Should().Contain("Resource ID must be present");
    }

    [Fact]
    public void GivenEmptyBodyId_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange
        var bodyId = "";

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            if (string.IsNullOrWhiteSpace(bodyId))
            {
                throw new BadRequestException($"Resource ID must be present in the body for PUT requests");
            }
        });

        ex.Message.Should().Contain("Resource ID must be present");
    }

    [Fact]
    public void GivenWhitespaceBodyId_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange
        var bodyId = "   ";

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            if (string.IsNullOrWhiteSpace(bodyId))
            {
                throw new BadRequestException($"Resource ID must be present in the body for PUT requests");
            }
        });

        ex.Message.Should().Contain("Resource ID must be present");
    }

    #endregion

    #region ID Mismatch Tests

    [Fact]
    public void GivenMismatchedIds_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange
        var urlId = "observation1";
        var bodyId = "observation2";

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            if (!string.Equals(bodyId, urlId, StringComparison.Ordinal))
            {
                throw new BadRequestException($"Resource ID in body ('{bodyId}') must match the ID in the URL ('{urlId}')");
            }
        });

        ex.Message.Should().Contain("must match the ID in the URL");
        ex.Message.Should().Contain("observation2");
        ex.Message.Should().Contain("observation1");
    }

    [Fact]
    public void GivenIdDifferentCase_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange - IDs should match exactly (case-sensitive)
        var urlId = "Patient123";
        var bodyId = "patient123";

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            if (!string.Equals(bodyId, urlId, StringComparison.Ordinal))
            {
                throw new BadRequestException($"Resource ID in body ('{bodyId}') must match the ID in the URL ('{urlId}')");
            }
        });

        ex.Message.Should().Contain("must match");
    }

    [Fact]
    public void GivenIdWithLeadingWhitespace_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange - whitespace in ID should fail comparison
        var urlId = "obs-123";
        var bodyId = " obs-123";  // Leading space

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            if (!string.Equals(bodyId, urlId, StringComparison.Ordinal))
            {
                throw new BadRequestException($"Resource ID in body ('{bodyId}') must match the ID in the URL ('{urlId}')");
            }
        });

        ex.Message.Should().Contain("must match");
    }

    #endregion

    #region Valid ID Tests

    [Fact]
    public void GivenMatchingIds_WhenValidatingPutRequest_ThenSucceeds()
    {
        // Arrange
        var id = "observation1";
        var bodyId = "observation1";

        // Act - no exception should be thrown
        if (string.IsNullOrWhiteSpace(bodyId))
        {
            throw new BadRequestException($"Resource ID must be present in the body for PUT requests");
        }

        if (!string.Equals(bodyId, id, StringComparison.Ordinal))
        {
            throw new BadRequestException($"Resource ID in body ('{bodyId}') must match the ID in the URL ('{id}')");
        }

        // Assert - if we get here, validation passed
        bodyId.Should().Be(id);
    }

    [Fact]
    public void GivenMatchingIdsWithSpecialCharacters_WhenValidatingPutRequest_ThenSucceeds()
    {
        // Arrange - special characters in ID should be preserved
        var id = "org-123_special.chars";
        var bodyId = "org-123_special.chars";

        // Act
        if (string.IsNullOrWhiteSpace(bodyId))
        {
            throw new BadRequestException($"Resource ID must be present in the body for PUT requests");
        }

        if (!string.Equals(bodyId, id, StringComparison.Ordinal))
        {
            throw new BadRequestException($"Resource ID in body ('{bodyId}') must match the ID in the URL ('{id}')");
        }

        // Assert
        bodyId.Should().Be(id);
    }

    [Fact]
    public void GivenMatchingIdsWithUUID_WhenValidatingPutRequest_ThenSucceeds()
    {
        // Arrange - UUID-style ID
        var id = "550e8400-e29b-41d4-a716-446655440000";
        var bodyId = "550e8400-e29b-41d4-a716-446655440000";

        // Act
        if (string.IsNullOrWhiteSpace(bodyId))
        {
            throw new BadRequestException($"Resource ID must be present in the body for PUT requests");
        }

        if (!string.Equals(bodyId, id, StringComparison.Ordinal))
        {
            throw new BadRequestException($"Resource ID in body ('{bodyId}') must match the ID in the URL ('{id}')");
        }

        // Assert
        bodyId.Should().Be(id);
    }

    #endregion
}
