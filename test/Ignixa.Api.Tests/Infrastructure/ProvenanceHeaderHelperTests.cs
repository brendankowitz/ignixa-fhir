// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.Infrastructure;
using Ignixa.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Xunit;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// Unit tests for ProvenanceHeaderHelper.
/// Tests parsing and validation of X-Provenance header.
/// </summary>
public class ProvenanceHeaderHelperTests
{
    private readonly RecyclableMemoryStreamManager _memoryStreamManager = new();
    private readonly NullLogger<ProvenanceHeaderHelperTests> _logger = NullLogger<ProvenanceHeaderHelperTests>.Instance;

    #region Valid Provenance Headers

    [Fact]
    public async Task GivenValidProvenanceHeader_WhenParsing_ThenReturnsProvenanceResource()
    {
        // Arrange
        var provenanceJson = """
        {
            "resourceType": "Provenance",
            "recorded": "2023-01-01T12:00:00Z",
            "agent": [
                {
                    "who": {
                        "reference": "Practitioner/123"
                    }
                }
            ]
        }
        """;
        var headers = new HeaderDictionary { { "X-Provenance", provenanceJson } };

        // Act
        var result = await ProvenanceHeaderHelper.TryParseProvenanceHeaderAsync(
            headers,
            _memoryStreamManager,
            _logger,
            CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result!.ResourceType.ShouldBe("Provenance");
    }

    [Fact]
    public async Task GivenNoProvenanceHeader_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var headers = new HeaderDictionary();

        // Act
        var result = await ProvenanceHeaderHelper.TryParseProvenanceHeaderAsync(
            headers,
            _memoryStreamManager,
            _logger,
            CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenEmptyProvenanceHeader_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var headers = new HeaderDictionary { { "X-Provenance", "" } };

        // Act
        var result = await ProvenanceHeaderHelper.TryParseProvenanceHeaderAsync(
            headers,
            _memoryStreamManager,
            _logger,
            CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    #endregion

    #region Invalid JSON

    [Fact]
    public async Task GivenInvalidJson_WhenParsing_ThenThrowsBadRequestException()
    {
        // Arrange
        var invalidJson = "{ invalid json }";
        var headers = new HeaderDictionary { { "X-Provenance", invalidJson } };

        // Act
        var act = async () => await ProvenanceHeaderHelper.TryParseProvenanceHeaderAsync(
            headers,
            _memoryStreamManager,
            _logger,
            CancellationToken.None);

        // Assert
        var ex = await Should.ThrowAsync<BadRequestException>(act);
        ex.Message.ShouldContain("invalid JSON");
    }

    #endregion

    #region Invalid Resource Type

    [Fact]
    public async Task GivenNonProvenanceResourceType_WhenParsing_ThenThrowsBadRequestException()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "name": [
                {
                    "family": "Doe"
                }
            ]
        }
        """;
        var headers = new HeaderDictionary { { "X-Provenance", patientJson } };

        // Act
        var act = async () => await ProvenanceHeaderHelper.TryParseProvenanceHeaderAsync(
            headers,
            _memoryStreamManager,
            _logger,
            CancellationToken.None);

        // Assert
        var ex = await Should.ThrowAsync<BadRequestException>(act);
        ex.Message.ShouldContain("must contain a Provenance resource");
    }

    #endregion

    #region Target Validation

    [Fact]
    public async Task GivenProvenanceWithTarget_WhenParsing_ThenThrowsBadRequestException()
    {
        // Arrange
        var provenanceWithTarget = """
        {
            "resourceType": "Provenance",
            "recorded": "2023-01-01T12:00:00Z",
            "target": [
                {
                    "reference": "Patient/123"
                }
            ],
            "agent": [
                {
                    "who": {
                        "reference": "Practitioner/123"
                    }
                }
            ]
        }
        """;
        var headers = new HeaderDictionary { { "X-Provenance", provenanceWithTarget } };

        // Act
        var act = async () => await ProvenanceHeaderHelper.TryParseProvenanceHeaderAsync(
            headers,
            _memoryStreamManager,
            _logger,
            CancellationToken.None);

        // Assert
        var ex = await Should.ThrowAsync<BadRequestException>(act);
        ex.Message.ShouldContain("must not specify 'target' property");
    }

    #endregion

    #region Header Length Validation

    [Fact]
    public async Task GivenExcessivelyLargeHeader_WhenParsing_ThenThrowsBadRequestException()
    {
        // Arrange
        var largeJson = """
        {
            "resourceType": "Provenance",
            "recorded": "2023-01-01T12:00:00Z",
            "agent": [
                {
                    "who": {
                        "reference": "Practitioner/123"
                    }
                }
            ],
            "text": {
                "status": "generated",
                "div": "
        """ + new string('X', 20000) + """
        "
            }
        }
        """;
        var headers = new HeaderDictionary { { "X-Provenance", largeJson } };

        // Act
        var act = async () => await ProvenanceHeaderHelper.TryParseProvenanceHeaderAsync(
            headers,
            _memoryStreamManager,
            _logger,
            CancellationToken.None);

        // Assert
        var ex = await Should.ThrowAsync<BadRequestException>(act);
        ex.Message.ShouldContain("too long");
    }

    #endregion
}
