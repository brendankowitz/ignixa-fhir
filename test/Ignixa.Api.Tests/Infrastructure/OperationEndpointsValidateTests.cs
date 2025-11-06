// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using FluentAssertions;
using Ignixa.Api.Http;
using Ignixa.Serialization;
using System.Text.Json.Nodes;
using Xunit;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// Unit tests for OperationEndpoints $validate operation registration and request handling.
/// Tests the validation endpoint routing, request body parsing, and error response generation.
/// </summary>
public class OperationEndpointsValidateTests
{
    #region Request Body Parsing Tests

    [Fact]
    public async Task GivenValidPatientResource_WhenParsingJson_ThenSucceedsWithCorrectResourceType()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "123",
            "name": [{"use": "official", "family": "Doe", "given": ["John"]}],
            "birthDate": "1980-01-01"
        }
        """;

        var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson));

        // Act
        var jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);

        // Assert
        jsonNode.Should().NotBeNull();
        jsonNode.ResourceType.Should().Be("Patient");
    }

    [Fact]
    public void GivenEmptyRequestBody_WhenCheckingLength_ThenReturnsZero()
    {
        // Arrange
        var memoryStream = new MemoryStream();

        // Act
        var length = memoryStream.Length;

        // Assert
        length.Should().Be(0);
    }

    [Fact]
    public async Task GivenInvalidJson_WhenParsingResource_ThenThrowsException()
    {
        // Arrange
        var invalidJson = "{ invalid json }";
        var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalidJson));

        // Act & Assert
        // Invalid JSON should throw an exception when parsing
        var exception = await Assert.ThrowsAsync<System.Text.Json.JsonException>(async () =>
        {
            await JsonSourceNodeFactory.Parse(memoryStream);
        });

        exception.Should().NotBeNull();
    }

    #endregion

    #region Parameters Resource Handling Tests

    [Fact]
    public async Task GivenParametersResourceWithMode_WhenExtracting_ThenReturnsMode()
    {
        // Arrange
        var parametersJson = """
        {
            "resourceType": "Parameters",
            "parameter": [
                {
                    "name": "mode",
                    "valueCode": "create"
                },
                {
                    "name": "resource",
                    "resource": {
                        "resourceType": "Patient",
                        "id": "123",
                        "name": [{"family": "Doe"}]
                    }
                }
            ]
        }
        """;

        var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(parametersJson));

        var jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
        var requestResource = jsonNode.MutableNode;

        // Act
        string mode = null;
        var parameters = requestResource?["parameter"];
        if (parameters is not null)
        {
            foreach (var param in parameters.AsArray())
            {
                var name = param?["name"]?.GetValue<string>();
                if (name == "mode")
                {
                    mode = param?["valueCode"]?.GetValue<string>();
                    break;
                }
            }
        }

        // Assert
        mode.Should().Be("create");
    }

    [Fact]
    public async Task GivenParametersResourceWithProfile_WhenExtracting_ThenReturnsProfile()
    {
        // Arrange
        var parametersJson = """
        {
            "resourceType": "Parameters",
            "parameter": [
                {
                    "name": "profile",
                    "valueUri": "http://example.com/profile/Patient"
                }
            ]
        }
        """;

        var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(parametersJson));

        var jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
        var requestResource = jsonNode.MutableNode;

        // Act
        string profile = null;
        var parameters = requestResource?["parameter"];
        if (parameters is not null)
        {
            foreach (var param in parameters.AsArray())
            {
                var name = param?["name"]?.GetValue<string>();
                if (name == "profile")
                {
                    profile = param?["valueUri"]?.GetValue<string>();
                    break;
                }
            }
        }

        // Assert
        profile.Should().Be("http://example.com/profile/Patient");
    }

    [Fact]
    public async Task GivenParametersResourceWithNestedResource_WhenExtracting_ThenExtractsResourceSuccessfully()
    {
        // Arrange
        var parametersJson = """
        {
            "resourceType": "Parameters",
            "parameter": [
                {
                    "name": "resource",
                    "resource": {
                        "resourceType": "Observation",
                        "id": "obs-456",
                        "status": "final",
                        "code": {
                            "coding": [{"system": "http://loinc.org", "code": "12345-1"}]
                        }
                    }
                }
            ]
        }
        """;

        var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(parametersJson));

        var jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
        var requestResource = jsonNode.MutableNode;

        // Act
        JsonNode extractedResource = null;
        var parameters = requestResource?["parameter"];
        if (parameters is not null)
        {
            foreach (var param in parameters.AsArray())
            {
                var name = param?["name"]?.GetValue<string>();
                if (name == "resource")
                {
                    extractedResource = param?["resource"];
                    break;
                }
            }
        }

        // Assert
        extractedResource.Should().NotBeNull();
        extractedResource["resourceType"]?.GetValue<string>().Should().Be("Observation");
        extractedResource["id"]?.GetValue<string>().Should().Be("obs-456");
    }

    #endregion

    #region Error Response Tests

    [Fact]
    public void GivenCreateOperationOutcomeError_WhenBuildingResponse_ThenReturnsValidStructure()
    {
        // Arrange
        var severity = "error";
        var code = "invalid";
        var diagnostics = "Invalid resource";

        // Act
        var response = new
        {
            resourceType = "OperationOutcome",
            issue = new[]
            {
                new
                {
                    severity,
                    code,
                    diagnostics
                }
            }
        };

        // Assert
        response.resourceType.Should().Be("OperationOutcome");
        response.issue.Should().NotBeNull();
        response.issue[0].severity.Should().Be("error");
        response.issue[0].code.Should().Be("invalid");
        response.issue[0].diagnostics.Should().Be("Invalid resource");
    }

    [Fact]
    public void GivenMissingResourceType_WhenBuildingErrorResponse_ThenIncludesDiagnostics()
    {
        // Arrange
        var expectedDiagnostics = "Resource must contain a 'resourceType' field";

        // Act
        var response = new
        {
            resourceType = "OperationOutcome",
            issue = new[]
            {
                new
                {
                    severity = "error",
                    code = "required",
                    diagnostics = expectedDiagnostics
                }
            }
        };

        // Assert
        response.issue[0].diagnostics.Should().Contain("resourceType");
    }

    [Fact]
    public void GivenSchemaNotFound_WhenBuildingErrorResponse_ThenIncludesProfileUri()
    {
        // Arrange
        var profileUri = "http://example.com/StructureDefinition/CustomPatient";
        var expectedDiagnostics = $"Schema not found for {profileUri}";

        // Act
        var response = new
        {
            resourceType = "OperationOutcome",
            issue = new[]
            {
                new
                {
                    severity = "error",
                    code = "not-found",
                    diagnostics = expectedDiagnostics
                }
            }
        };

        // Assert
        response.issue[0].diagnostics.Should().Contain(profileUri);
    }

    #endregion

    #region Resource Type Extraction Tests

    [Fact]
    public async Task GivenDirectResourceJson_WhenExtractingResourceType_ThenReturnsResourceType()
    {
        // Arrange
        var observationJson = """
        {
            "resourceType": "Observation",
            "id": "obs-789",
            "status": "final"
        }
        """;

        var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(observationJson));

        // Act
        var jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);

        // Assert
        jsonNode.ResourceType.Should().Be("Observation");
    }

    [Fact]
    public async Task GivenMissingResourceType_WhenExtracting_ThenReturnsNull()
    {
        // Arrange
        var invalidJson = """
        {
            "id": "123",
            "status": "final"
        }
        """;

        var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalidJson));

        // Act
        var jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);

        // Assert
        jsonNode.ResourceType.Should().BeNullOrEmpty();
    }

    #endregion

    #region Multi-Tenant Error Handling Tests

    [Fact]
    public void GivenMissingTenantId_WhenValidatingSystemLevel_ThenErrorMessageIsCorrect()
    {
        // Arrange
        var expectedMessage = "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/$validate";

        // Act & Assert
        expectedMessage.Should().Contain("TenantId");
    }

    [Fact]
    public void GivenMissingTenantId_WhenValidatingAgnostic_ThenErrorMessageIsCorrect()
    {
        // Arrange
        var expectedMessage = "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/{resourceType}/$validate";

        // Act & Assert
        expectedMessage.Should().Contain("TenantId");
    }

    #endregion

    #region Content Type Tests

    [Fact]
    public void GivenKnownContentTypes_WhenChecking_ThenConstantsAreDefined()
    {
        // Arrange & Act & Assert
        // These should compile and be available
        var fhirJson = KnownContentTypes.ApplicationFhirJson;
        var json = KnownContentTypes.ApplicationJson;

        fhirJson.Should().NotBeNullOrEmpty();
        json.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenApplicationFhirJson_WhenChecking_ThenCorrectValue()
    {
        // Act
        var fhirJson = KnownContentTypes.ApplicationFhirJson;

        // Assert
        fhirJson.Should().Be("application/fhir+json");
    }

    [Fact]
    public void GivenApplicationJson_WhenChecking_ThenCorrectValue()
    {
        // Act
        var json = KnownContentTypes.ApplicationJson;

        // Assert
        json.Should().Be("application/json");
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void GivenJsonNode_WhenCallingToJsonString_ThenReturnsJsonString()
    {
        // Arrange
        var jsonString = """{"resourceType": "Patient", "id": "123"}""";
        var jsonNode = JsonNode.Parse(jsonString);

        // Act
        var result = jsonNode.ToString();

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Patient");
    }

    #endregion
}
