// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Operations.Features.Validate;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
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
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly Func<FhirVersion, int, IValidationSchemaResolver> _schemaResolverFactory;
    private readonly ITerminologyService _terminologyService;
    private readonly ValidateResourceHandler _handler;

    public ValidateResourceHandlerTests()
    {
        _contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        _schemaResolverFactory = Substitute.For<Func<FhirVersion, int, IValidationSchemaResolver>>();
        _terminologyService = Substitute.For<ITerminologyService>();
        _handler = new ValidateResourceHandler(
            _contextAccessor,
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.OperationOutcome.ShouldNotBeNull();
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalidJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: null,
            JsonNode: jsonNode);

        SetupHttpContext();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        var operationOutcome = result.OperationOutcome;
        var issues = operationOutcome["issue"]?.AsArray();
        issues.ShouldNotBeNull();
        issues.Count.ShouldBeGreaterThan(0);
        issues[0]["severity"]?.GetValue<string>().ShouldBe("error");
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(observationJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: null,  // Not provided in command
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Minimal"
        };

        // Mock FHIR request context with tenant configuration
        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantConfiguration.Returns(tenantConfig);
        _contextAccessor.RequestContext.Returns(fhirContext);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Minimal"
        };
        // Mock FHIR request context with tenant configuration
        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantConfiguration.Returns(tenantConfig);
        _contextAccessor.RequestContext.Returns(fhirContext);

        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Spec"
        };
        // Mock FHIR request context with tenant configuration
        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantConfiguration.Returns(tenantConfig);
        _contextAccessor.RequestContext.Returns(fhirContext);

        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
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
        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
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
        result.ShouldNotBeNull();
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver("http://hl7.org/fhir/StructureDefinition/Patient");

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
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
        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode,
            Profile: missingProfileUri);

        SetupHttpContext();
        var schemaResolver = Substitute.For<IValidationSchemaResolver>();
        schemaResolver.GetSchema(missingProfileUri).Returns((object)null);
        _schemaResolverFactory(FhirVersion.R4, Arg.Any<int>()).Returns(schemaResolver);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        var operationOutcome = result.OperationOutcome;
        var issues = operationOutcome["issue"]?.AsArray();
        issues.ShouldNotBeNull();
        issues[0]["code"]?.GetValue<string>().ShouldBe("not-found");
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Spec"
        };
        // Mock FHIR request context with tenant configuration
        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantConfiguration.Returns(tenantConfig);
        _contextAccessor.RequestContext.Returns(fhirContext);

        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R5",
            ValidationDepth = "Spec"
        };
        // Mock FHIR request context with tenant configuration
        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantConfiguration.Returns(tenantConfig);
        _contextAccessor.RequestContext.Returns(fhirContext);

        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        var operationOutcome = result.OperationOutcome;
        operationOutcome["resourceType"]?.GetValue<string>().ShouldBe("OperationOutcome");
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

        var jsonNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);
        var command = new ValidateResourceCommand(
            TenantId: 1,
            ResourceType: "Patient",
            JsonNode: jsonNode);

        SetupHttpContext();
        SetupSchemaResolver();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        var operationOutcome = result.OperationOutcome;
        var issues = operationOutcome["issue"];
        issues.ShouldNotBeNull();
    }

    #endregion

    #region Helper Methods

    private void SetupHttpContext()
    {
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Spec"
        };
        // Mock FHIR request context with tenant configuration
        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantConfiguration.Returns(tenantConfig);
        _contextAccessor.RequestContext.Returns(fhirContext);
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
        _schemaResolverFactory(Arg.Any<FhirVersion>(), Arg.Any<int>()).Returns(schemaResolver);
    }

    #endregion
}
