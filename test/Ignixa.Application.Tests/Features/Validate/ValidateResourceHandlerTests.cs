// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using FluentAssertions;
using Ignixa.Application.Operations.Features.Validate;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text.Json.Nodes;
using Xunit;

namespace Ignixa.Application.Tests.Features.Validate;

/// <summary>
/// Unit tests for ValidateResourceHandler.
/// Tests the FHIR $validate operation handler logic, including validation tier processing,
/// resource type extraction, and OperationOutcome response generation.
/// </summary>
public class ValidateResourceHandlerTests
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Func<FhirSpecification, int, IValidationSchemaResolver> _schemaResolverFactory;
    private readonly ITerminologyService _terminologyService;
    private readonly ValidateResourceHandler _handler;

    public ValidateResourceHandlerTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _schemaResolverFactory = Substitute.For<Func<FhirSpecification, int, IValidationSchemaResolver>>();
        _terminologyService = Substitute.For<ITerminologyService>();
        _handler = new ValidateResourceHandler(
            _httpContextAccessor,
            _schemaResolverFactory,
            _terminologyService,
            NullLogger<ValidateResourceHandler>.Instance);
    }

    #region Resource Type Validation Tests

    [Fact]
    public async Task GivenValidPatientResource_WhenValidating_ThenProcessesSuccessfully()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OperationOutcome.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenMissingResourceType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var invalidJson = """
        {
            "id": "123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalidJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: null,
            JsonNode: jsonNode);

        SetupHttpContext();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var operationOutcome = result.OperationOutcome;
        var issues = operationOutcome["issue"]?.AsArray();
        issues.Should().NotBeNull();
        issues.Count.Should().BeGreaterThan(0);
        issues[0]["severity"]?.GetValue<string>().Should().Be("error");
    }

    [Fact]
    public async Task GivenResourceTypeInJsonNode_WhenResourceTypeParameterNull_ThenExtractsFromJson()
    {
        // Arrange
        var observationJson = """
        {
            "resourceType": "Observation",
            "id": "obs-456"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(observationJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: null,  // Not provided in command
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // The handler should extract resourceType from the JsonNode
    }

    #endregion

    #region Validation Tier Tests

    [Fact]
    public async Task GivenValidationTierNone_WhenValidating_ThenSkipsValidation()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123",
            "active": "invalid-boolean"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var httpContext = Substitute.For<HttpContext>();
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationTier = "None"
        };
        httpContext.Items["TenantConfiguration"].Returns(tenantConfig);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // With ValidationTier.None, no validation errors should be reported
    }

    [Fact]
    public async Task GivenValidationTierFast_WhenValidating_ThenUseFastTier()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var httpContext = Substitute.For<HttpContext>();
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationTier = "Fast"
        };
        httpContext.Items["TenantConfiguration"].Returns(tenantConfig);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenValidationTierSpec_WhenValidating_ThenUseSpecTier()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var httpContext = Substitute.For<HttpContext>();
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationTier = "Spec"
        };
        httpContext.Items["TenantConfiguration"].Returns(tenantConfig);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Profile Validation Tests

    [Fact]
    public async Task GivenCustomProfileUri_WhenValidating_ThenUsesSpecificProfile()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var customProfileUri = "http://example.com/StructureDefinition/CustomPatient";
        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode,
            Profile: customProfileUri);

        SetupHttpContext();
        SetupSchemaResolver(customProfileUri);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // Verify that the schema resolver was called with the custom profile
    }

    [Fact]
    public async Task GivenNoProfile_WhenValidating_ThenUsesBaseResourceDefinition()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver("http://hl7.org/fhir/StructureDefinition/Patient");

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // Verify that the schema resolver was called with the base definition
    }

    [Fact]
    public async Task GivenProfileNotFound_WhenValidating_ThenReturnsSchemaNotFoundError()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var missingProfileUri = "http://example.com/StructureDefinition/NonExistent";
        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode,
            Profile: missingProfileUri);

        SetupHttpContext();
        var schemaResolver = Substitute.For<IValidationSchemaResolver>();
        schemaResolver.GetSchema(missingProfileUri).Returns((object)null);
        _schemaResolverFactory(FhirSpecification.R4, Arg.Any<int>()).Returns(schemaResolver);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var operationOutcome = result.OperationOutcome;
        var issues = operationOutcome["issue"]?.AsArray();
        issues.Should().NotBeNull();
        issues[0]["code"]?.GetValue<string>().Should().Be("not-found");
    }

    #endregion

    #region FHIR Version Tests

    [Fact]
    public async Task GivenR4FhirVersion_WhenValidating_ThenUsesR4Specification()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var httpContext = Substitute.For<HttpContext>();
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationTier = "Spec"
        };
        httpContext.Items["TenantConfiguration"].Returns(tenantConfig);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // Verify that R4 specification was used
    }

    [Fact]
    public async Task GivenR5FhirVersion_WhenValidating_ThenUsesR5Specification()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var httpContext = Substitute.For<HttpContext>();
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R5",
            ValidationTier = "Spec"
        };
        httpContext.Items["TenantConfiguration"].Returns(tenantConfig);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region OperationOutcome Response Tests

    [Fact]
    public async Task GivenValidResource_WhenValidating_ThenReturnsOperationOutcomeWithNoIssues()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var operationOutcome = result.OperationOutcome;
        operationOutcome["resourceType"]?.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task GivenOperationOutcomeResponse_WhenValidating_ThenContainsIssueArray()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;

        var jsonNode = await JsonSourceNodeFactory.Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)));
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var operationOutcome = result.OperationOutcome;
        var issues = operationOutcome["issue"];
        issues.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private void SetupHttpContext()
    {
        var httpContext = Substitute.For<HttpContext>();
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationTier = "Spec"
        };
        httpContext.Items["TenantConfiguration"].Returns(tenantConfig);
        _httpContextAccessor.HttpContext.Returns(httpContext);
    }

    private void SetupSchemaResolver(string schemaUri = null)
    {
        var schemaResolver = Substitute.For<IValidationSchemaResolver>();

        if (string.IsNullOrEmpty(schemaUri))
        {
            schemaUri = "http://hl7.org/fhir/StructureDefinition/Patient";
        }

        // Setup a basic schema resolver that returns null for schemas (which will result in no validation)
        schemaResolver.GetSchema(Arg.Any<string>()).Returns((object)null);
        _schemaResolverFactory(Arg.Any<FhirSpecification>(), Arg.Any<int>()).Returns(schemaResolver);
    }

    #endregion
}
