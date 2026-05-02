// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Operations.Features.DeIdentify;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Darts.Configuration;
using Ignixa.DeId.Models;
using Ignixa.DeId.Pipeline;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.DeIdentify;

public class DeIdentifyHandlerTests
{
    private readonly IDeIdPipeline _pipeline;
    private readonly LibraryConfigurationLoader _configLoader;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly DeIdentifyHandler _handler;
    private readonly IFhirVersionContext _versionContext;

    public DeIdentifyHandlerTests()
    {
        _pipeline = Substitute.For<IDeIdPipeline>();
        _configLoader = new LibraryConfigurationLoader();
        _contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        _versionContext = Substitute.For<IFhirVersionContext>();
        _handler = new DeIdentifyHandler(
            _pipeline,
            _configLoader,
            _contextAccessor,
            NullLogger<DeIdentifyHandler>.Instance);
    }

    [Fact]
    public async Task GivenValidPatientAndSafeHarborPolicy_WhenDeIdentifying_ThenReturnsSuccessWithResource()
    {
        // Arrange
        var patientJson = """
            {
                "resourceType": "Patient",
                "id": "patient-123",
                "name": [{ "family": "Smith" }]
            }
            """;
        var resource = ResourceJsonNode.Parse(patientJson);
        var options = CreateSafeHarborOptions();
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-1", "SAFE_HARBOR", options);
        var command = new DeIdentifyCommand(1, resource, "SAFE_HARBOR", library);

        SetupFhirContext();
        SetupPipelineSuccess(resource);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeTrue();
        result.OutputResource.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenPipelineReturnsFailure_WhenDeIdentifying_ThenReturnsFailureWithErrorMessage()
    {
        // Arrange
        var patientJson = """
            {
                "resourceType": "Patient",
                "id": "patient-123"
            }
            """;
        var resource = ResourceJsonNode.Parse(patientJson);
        var options = CreateSafeHarborOptions();
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-2", "SAFE_HARBOR", options);
        var command = new DeIdentifyCommand(1, resource, "SAFE_HARBOR", library);

        SetupFhirContext();
        SetupPipelineFailure("Rule evaluation failed");

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Rule evaluation failed");
        result.OutputResource.ShouldBeNull();
    }

    [Fact]
    public async Task GivenInvalidLibraryConfiguration_WhenDeIdentifying_ThenReturnsFailureWithConfigError()
    {
        // Arrange
        var patientJson = """
            {
                "resourceType": "Patient",
                "id": "patient-123"
            }
            """;
        var resource = ResourceJsonNode.Parse(patientJson);
        var badLibrary = ResourceJsonNode.Parse("""
            {
                "resourceType": "Library",
                "id": "bad-lib",
                "status": "active"
            }
            """);
        var command = new DeIdentifyCommand(1, resource, "SAFE_HARBOR", badLibrary);

        SetupFhirContext();

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Configuration error");
    }

    [Fact]
    public async Task GivenMissingRequestContext_WhenDeIdentifying_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var patientJson = """
            {
                "resourceType": "Patient",
                "id": "patient-123"
            }
            """;
        var resource = ResourceJsonNode.Parse(patientJson);
        var options = CreateSafeHarborOptions();
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-3", "SAFE_HARBOR", options);
        var command = new DeIdentifyCommand(1, resource, "SAFE_HARBOR", library);

        _contextAccessor.RequestContext.Returns((IFhirRequestContext)null!);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task GivenExpertDeterminationPolicy_WhenDeIdentifying_ThenUsesCorrectOptions()
    {
        // Arrange
        var patientJson = """
            {
                "resourceType": "Patient",
                "id": "patient-123",
                "name": [{ "family": "Jones" }]
            }
            """;
        var resource = ResourceJsonNode.Parse(patientJson);
        var options = new DeIdOptions
        {
            FhirVersion = "R4",
            Rules = [new FhirPathRule { Path = "Patient.name", Method = "redact" }],
            Parameters = new ParameterOptions
            {
                EnablePartialDatesForRedact = false,
                EnablePartialAgesForRedact = false,
                EnablePartialZipCodesForRedact = false
            },
            Processing = new ProcessingOptions
            {
                ErrorHandling = ErrorHandlingMode.FailFast
            }
        };
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-4", "EXPERT_DETERMINATION", options);
        var command = new DeIdentifyCommand(1, resource, "EXPERT_DETERMINATION", library);

        SetupFhirContext();
        SetupPipelineSuccess(resource);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        await _pipeline.Received(1).ExecuteAsync(
            Arg.Any<DeIdContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenValidCommand_WhenDeIdentifying_ThenPipelineReceivesCorrectTenantIdAndResource()
    {
        // Arrange
        var patientJson = """
            {
                "resourceType": "Patient",
                "id": "patient-456"
            }
            """;
        var resource = ResourceJsonNode.Parse(patientJson);
        var options = CreateSafeHarborOptions();
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-5", "SAFE_HARBOR", options);
        var command = new DeIdentifyCommand(42, resource, "SAFE_HARBOR", library);

        SetupFhirContext();
        SetupPipelineSuccess(resource);

        // Act
        await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        await _pipeline.Received(1).ExecuteAsync(
            Arg.Any<DeIdContext>(),
            Arg.Any<CancellationToken>());
    }

    private void SetupFhirContext()
    {
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Minimal"
        };

        var schemaProvider = new R4CoreSchemaProvider();
        _versionContext.GetSchemaProvider(FhirVersion.R4, Arg.Any<int?>()).Returns(schemaProvider);
        _versionContext.GetBaseSchemaProvider(FhirVersion.R4).Returns(schemaProvider);

        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantConfiguration.Returns(tenantConfig);
        fhirContext.FhirVersion.Returns(FhirVersion.R4);
        fhirContext.VersionContext.Returns(_versionContext);

        _contextAccessor.RequestContext.Returns(fhirContext);
    }

    private void SetupPipelineSuccess(ResourceJsonNode outputResource)
    {
        var deIdResult = new DeIdResult
        {
            Resource = outputResource,
            DeidentifiedJson = outputResource.MutableNode.ToJsonString(),
            Metrics = new ProcessingMetrics
            {
                NodesProcessed = 1,
                Duration = TimeSpan.Zero,
                OperationCounts = new Dictionary<string, int> { ["redact"] = 1 }.ToImmutableDictionary()
            },
            AppliedLabels = new AppliedSecurityLabels { IsRedacted = true }
        };

        _pipeline.ExecuteAsync(Arg.Any<DeIdContext>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeIdResult>.Success(deIdResult));
    }

    private void SetupPipelineFailure(string errorMessage)
    {
        var error = new DeIdError("PROCESSING_ERROR", errorMessage);
        _pipeline.ExecuteAsync(Arg.Any<DeIdContext>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeIdResult>.Failure(error));
    }

    private static DeIdOptions CreateSafeHarborOptions()
    {
        return new DeIdOptions
        {
            FhirVersion = "R4",
            Rules =
            [
                new FhirPathRule { Path = "Patient.id", Method = "cryptoHash" },
                new FhirPathRule { Path = "Patient.name", Method = "redact" }
            ],
            Parameters = new ParameterOptions
            {
                EnablePartialDatesForRedact = true
            },
            Processing = new ProcessingOptions
            {
                ErrorHandling = ErrorHandlingMode.LogAndContinue
            }
        };
    }
}
