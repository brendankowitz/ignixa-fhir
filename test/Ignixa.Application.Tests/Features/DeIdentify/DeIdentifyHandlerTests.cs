// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Operations.Features.DeIdentify;
using Ignixa.Application.Tests.TestDoubles;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Darts;
using Ignixa.DeId.Darts.Configuration;
using Ignixa.DeId.Models;
using Ignixa.DeId.Pipeline;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Ignixa.Serialization.TestSupport;

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
    public async Task GivenPipelineSucceedsWithWarnings_WhenDeIdentifying_ThenWarningsAreReturnedToTheCaller()
    {
        // Regression: the handler returned only the resource, dropping every warning.
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-123"}""");
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-w", "SAFE_HARBOR", CreateSafeHarborOptions());
        var command = new DeIdentifyCommand(1, resource, "SAFE_HARBOR", library);

        SetupFhirContext();
        SetupPipelineSuccess(resource, "Processor failed for rule 'Patient.name'");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Warnings.ShouldHaveSingleItem().ShouldBe("Processor failed for rule 'Patient.name'");
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
    public async Task GivenARuleThatExceededAnEvaluationLimit_WhenDeIdentifying_ThenTheRequestIsTooCostly()
    {
        // Arrange: the rule failed closed with the limit as its cause, wrapped as the pipeline reports it.
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-123"}""");
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-4", "SAFE_HARBOR", CreateSafeHarborOptions());
        var command = new DeIdentifyCommand(1, resource, "SAFE_HARBOR", library);
        SetupFhirContext();
        var cause = new InvalidOperationException(
            "rule failed", new Ignixa.FhirPath.Evaluation.FhirPathResourceLimitExceededException("step budget exceeded"));
        _pipeline.ExecuteAsync(Arg.Any<DeIdContext>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeIdResult>.Failure(new DeIdError("RULE_EVALUATION_FAILED", "rule failed", ErrorSeverity.Error, cause)));

        // Act & Assert
        await Should.ThrowAsync<Ignixa.Domain.Exceptions.RequestTooCostlyException>(
            () => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task GivenPipelineFailureWhoseMessageQuotesData_WhenDeIdentifying_ThenLogOmitsTheMessageAndCallerStillReceivesIt()
    {
        // Arrange
        const string sensitive = "Zebra-Sensitive-Value";
        var logger = new CapturingLogger<DeIdentifyHandler>();
        var handler = new DeIdentifyHandler(_pipeline, _configLoader, _contextAccessor, logger);
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-123"}""");
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-3", "SAFE_HARBOR", CreateSafeHarborOptions());
        var command = new DeIdentifyCommand(1, resource, "SAFE_HARBOR", library);
        SetupFhirContext();
        SetupPipelineFailure($"The value '{sensitive}' was not recognized");

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        logger.Messages.ShouldNotBeEmpty();
        logger.Messages.ShouldAllBe(message => !message.Contains(sensitive));
        result.ErrorMessage.ShouldContain(sensitive);
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

    [Theory]
    [InlineData(DartsConstants.PolicySafeHarbor)]
    [InlineData(DartsConstants.PolicyExpertDetermination)]
    public async Task GivenNoConfigurationLibrary_WhenDeIdentifyingWithABuiltInPolicy_ThenItsRulesAreEvaluatedAsServerDefined(string policy)
    {
        // Regression: the built-in policy was round-tripped through a Library, which dropped the rules' server-defined
        // provenance, so their paths shared the bounded request-supplied expression tier with custom-rule traffic.
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-123"}""");
        var command = new DeIdentifyCommand(1, resource, policy, null);
        SetupFhirContext();
        SetupPipelineSuccess(resource);

        await _handler.HandleAsync(command, CancellationToken.None);

        var options = ReceivedOptions();
        options.Rules.ShouldNotBeEmpty();
        options.Rules.ShouldAllBe(rule => rule.IsServerDefined);
    }

    [Fact]
    public async Task GivenNoConfigurationLibraryAndSafeHarborPolicy_WhenDeIdentifying_ThenProcessorFailuresStopTheOperation()
    {
        // Regression (moved from the endpoint, which no longer builds the policy): the default SAFE_HARBOR policy
        // loaded with LogAndContinue, so by default a processor failure left data in clear and returned 200.
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-123"}""");
        var command = new DeIdentifyCommand(1, resource, DartsConstants.PolicySafeHarbor, null);
        SetupFhirContext();
        SetupPipelineSuccess(resource);

        await _handler.HandleAsync(command, CancellationToken.None);

        ReceivedOptions().Processing!.ErrorHandling.ShouldBe(ErrorHandlingMode.StopOnError);
    }

    [Fact]
    public async Task GivenNoConfigurationLibraryAndAnUnknownPolicy_WhenDeIdentifying_ThenTheRequestIsRejectedNamingTheSupportedPolicies()
    {
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-123"}""");
        var command = new DeIdentifyCommand(1, resource, "NO_SUCH_POLICY", null);
        SetupFhirContext();

        var ex = await Should.ThrowAsync<BadRequestException>(() => _handler.HandleAsync(command, CancellationToken.None));

        ex.Message.ShouldBe(
            $"Unknown de-identification policy: 'NO_SUCH_POLICY'. Supported policies: {DartsConstants.PolicySafeHarbor}, {DartsConstants.PolicyExpertDetermination}.");
        await _pipeline.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task GivenConfigurationLibraryClaimingServerDefinedRules_WhenDeIdentifying_ThenItsRulesStayRequestSupplied()
    {
        // Guard: a request-supplied Library cannot promote its rules into the server expression tier.
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-123"}""");
        const string optionsJson = """
            {"fhirVersion":"R4","fhirPathRules":[{"path":"Patient.name","method":"redact","isServerDefined":true}]}
            """;
        var library = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Library",
                "status": "active",
                "type": { "coding": [{ "system": "{{DartsConstants.LibraryTypeSystem}}", "code": "{{DartsConstants.LibraryTypeCode}}" }] },
                "content": [{ "contentType": "application/json", "data": "{{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(optionsJson))}}" }]
            }
            """);
        var command = new DeIdentifyCommand(1, resource, DartsConstants.PolicySafeHarbor, library);
        SetupFhirContext();
        SetupPipelineSuccess(resource);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.ErrorMessage.ShouldBeNull();
        ReceivedOptions().Rules.ShouldHaveSingleItem().IsServerDefined.ShouldBeFalse();
    }

    private DeIdOptions ReceivedOptions()
    {
        var call = _pipeline.ReceivedCalls().ShouldHaveSingleItem();
        return ((DeIdContext)call.GetArguments()[0]!).Options;
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

    private void SetupPipelineSuccess(ResourceJsonNode outputResource, params string[] warnings)
    {
        var deIdResult = new DeIdResult
        {
            Resource = outputResource,
            DeidentifiedJson = outputResource.MutableNode().ToJsonString(),
            Metrics = new ProcessingMetrics
            {
                NodesProcessed = 1,
                Duration = TimeSpan.Zero,
                OperationCounts = new Dictionary<string, int> { ["redact"] = 1 }.ToImmutableDictionary()
            },
            Warnings = [.. warnings],
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
