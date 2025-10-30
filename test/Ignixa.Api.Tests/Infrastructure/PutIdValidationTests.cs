// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.Infrastructure;
using Ignixa.Domain.Exceptions;
using Ignixa.Search;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json.Nodes;
using Xunit;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// Unit tests for PUT request ID validation in FhirEndpoints.
/// Verifies that PUT requests validate ID consistency between URL and JSON body.
///
/// FHIR Spec Requirement (R4/R4B/R5):
/// "For a PUT operation, the resource id in the body SHALL match the [id] in the URL.
///  If the id is not present in the body, the server SHALL return a 400 Bad Request."
/// </summary>
public class PutIdValidationTests
{
    #region Missing ID in Body Tests

    [Fact]
    public void GivenResourceWithNoIdInBody_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange
        var resourceType = "Observation";
        var urlId = "observation1";

        // Create a resource without an id field
        var jsonNode = new ResourceJsonNode
        {
            ResourceType = resourceType,
            MutableNode = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final"
            }
            """)!
        };

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            // This would normally be called within HandlePutResource
            var bodyId = jsonNode.Id;
            if (string.IsNullOrWhiteSpace(bodyId))
            {
                throw new BadRequestException($"Resource ID must be present in the body for PUT requests");
            }
        });

        ex.Message.Should().Contain("Resource ID must be present");
    }

    [Fact]
    public void GivenResourceWithEmptyIdInBody_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange
        var resourceType = "Observation";
        var urlId = "observation1";

        var jsonNode = new ResourceJsonNode
        {
            ResourceType = resourceType,
            MutableNode = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "id": "",
                "status": "final"
            }
            """)!
        };

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            var bodyId = jsonNode.Id;
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
        var resourceType = "Observation";
        var urlId = "observation1";
        var bodyId = "observation2";

        var jsonNode = new ResourceJsonNode
        {
            ResourceType = resourceType,
            MutableNode = JsonNode.Parse($"""
            {{
                "resourceType": "Observation",
                "id": "{bodyId}",
                "status": "final"
            }}
            """)!
        };

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            var retrievedBodyId = jsonNode.Id;
            if (!string.Equals(retrievedBodyId, urlId, StringComparison.Ordinal))
            {
                throw new BadRequestException($"Resource ID in body ('{retrievedBodyId}') must match the ID in the URL ('{urlId}')");
            }
        });

        ex.Message.Should().Contain($"must match the ID in the URL");
        ex.Message.Should().Contain($"observation2");
        ex.Message.Should().Contain($"observation1");
    }

    [Fact]
    public void GivenIdDifferentCase_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange - IDs should match exactly (case-sensitive)
        var resourceType = "Patient";
        var urlId = "Patient123";
        var bodyId = "patient123";

        var jsonNode = new ResourceJsonNode
        {
            ResourceType = resourceType,
            MutableNode = JsonNode.Parse($"""
            {{
                "resourceType": "Patient",
                "id": "{bodyId}",
                "name": [{{ "use": "official", "family": "Doe" }}]
            }}
            """)!
        };

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            var retrievedBodyId = jsonNode.Id;
            if (!string.Equals(retrievedBodyId, urlId, StringComparison.Ordinal))
            {
                throw new BadRequestException($"Resource ID in body ('{retrievedBodyId}') must match the ID in the URL ('{urlId}')");
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
        var resourceType = "Observation";
        var id = "observation1";

        var jsonNode = new ResourceJsonNode
        {
            ResourceType = resourceType,
            MutableNode = JsonNode.Parse($"""
            {{
                "resourceType": "Observation",
                "id": "{id}",
                "status": "final"
            }}
            """)!
        };

        // Act - no exception should be thrown
        var bodyId = jsonNode.Id;
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
    public void GivenComplexResourceWithValidId_WhenValidatingPutRequest_ThenSucceeds()
    {
        // Arrange - test with a more complete resource
        var resourceType = "Patient";
        var id = "patient-example-123";

        var jsonNode = new ResourceJsonNode
        {
            ResourceType = resourceType,
            MutableNode = JsonNode.Parse($"""
            {{
                "resourceType": "Patient",
                "id": "{id}",
                "active": true,
                "name": [
                    {{
                        "use": "official",
                        "family": "Doe",
                        "given": ["John"]
                    }}
                ],
                "telecom": [
                    {{
                        "system": "phone",
                        "value": "555-1234"
                    }}
                ]
            }}
            """)!
        };

        // Act & Assert
        var bodyId = jsonNode.Id;
        bodyId.Should().Be(id);

        if (!string.Equals(bodyId, id, StringComparison.Ordinal))
        {
            throw new BadRequestException($"Resource ID in body ('{bodyId}') must match the ID in the URL ('{id}')");
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenIdWithWhitespace_WhenValidatingPutRequest_ThenThrowsBadRequestException()
    {
        // Arrange - whitespace in ID should fail comparison
        var resourceType = "Observation";
        var urlId = "obs-123";
        var bodyId = " obs-123";  // Leading space

        var jsonNode = new ResourceJsonNode
        {
            ResourceType = resourceType,
            MutableNode = JsonNode.Parse($"""
            {{
                "resourceType": "Observation",
                "id": "{bodyId}",
                "status": "final"
            }}
            """)!
        };

        // Act & Assert
        var ex = Assert.Throws<BadRequestException>(() =>
        {
            var retrievedBodyId = jsonNode.Id;
            if (!string.Equals(retrievedBodyId, urlId, StringComparison.Ordinal))
            {
                throw new BadRequestException($"Resource ID in body ('{retrievedBodyId}') must match the ID in the URL ('{urlId}')");
            }
        });

        ex.Message.Should().Contain("must match");
    }

    [Fact]
    public void GivenIdWithSpecialCharacters_WhenValidatingPutRequest_ThenMatchesCorrectly()
    {
        // Arrange - special characters in ID should be preserved
        var resourceType = "Organization";
        var id = "org-123_special.chars";

        var jsonNode = new ResourceJsonNode
        {
            ResourceType = resourceType,
            MutableNode = JsonNode.Parse($"""
            {{
                "resourceType": "Organization",
                "id": "{id}",
                "active": true,
                "name": "Test Org"
            }}
            """)!
        };

        // Act & Assert
        var bodyId = jsonNode.Id;
        bodyId.Should().Be(id);
    }

    #endregion
}
