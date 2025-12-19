// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Application.Features.Experimental.Ips.Generator;
using Ignixa.Domain.Exceptions;
using Ignixa.Serialization.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Experimental.Tests.Features.Ips;

/// <summary>
/// Unit tests for <see cref="IpsGeneratorHandler"/>.
/// </summary>
public class IpsGeneratorHandlerTests
{
    private readonly IIpsGenerationStrategyRegistry _strategyRegistry;
    private readonly IIpsGeneratorService _generatorService;
    private readonly ILogger<IpsGeneratorHandler> _logger;
    private readonly IpsGeneratorHandler _handler;

    public IpsGeneratorHandlerTests()
    {
        _strategyRegistry = Substitute.For<IIpsGenerationStrategyRegistry>();
        _generatorService = Substitute.For<IIpsGeneratorService>();
        _logger = NullLogger<IpsGeneratorHandler>.Instance;

        // Setup default strategy
        var defaultStrategy = Substitute.For<IIpsGenerationStrategy>();
        defaultStrategy.BundleProfile.Returns("http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips");
        _strategyRegistry.GetDefaultStrategy().Returns(defaultStrategy);

        _handler = new IpsGeneratorHandler(_strategyRegistry, _generatorService, _logger);
    }

    [Fact]
    public async Task GivenMissingPatientIdAndIdentifier_WhenHandling_ThenThrowsBadRequestException()
    {
        // Arrange
        var query = new IpsGeneratorQuery(
            PatientId: null,
            PatientIdentifier: null,
            Profile: null);

        // Act & Assert
        var exception = await Should.ThrowAsync<BadRequestException>(
            () => _handler.HandleAsync(query, CancellationToken.None));

        exception.Message.ShouldContain("PatientId or PatientIdentifier");
    }

    [Fact]
    public async Task GivenValidPatientId_WhenHandling_ThenCallsGeneratorServiceWithId()
    {
        // Arrange
        var query = new IpsGeneratorQuery(
            PatientId: "patient-123",
            PatientIdentifier: null,
            Profile: null);

        var mockBundle = CreateMockBundle();
        _generatorService.GenerateIpsAsync(
            "patient-123",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockBundle));

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        await _generatorService.Received(1).GenerateIpsAsync(
            "patient-123",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>());

        result.ShouldNotBeNull();
        result.IpsBundle.ShouldBe(mockBundle);
    }

    [Fact]
    public async Task GivenPatientIdWithProfile_WhenHandling_ThenPassesProfileToService()
    {
        // Arrange
        var customStrategy = Substitute.For<IIpsGenerationStrategy>();
        customStrategy.BundleProfile.Returns("http://example.org/profile");
        _strategyRegistry.GetStrategy("http://example.org/profile").Returns(customStrategy);

        var query = new IpsGeneratorQuery(
            PatientId: "patient-123",
            PatientIdentifier: null,
            Profile: "http://example.org/profile");

        var mockBundle = CreateMockBundle();
        _generatorService.GenerateIpsAsync("patient-123", "http://example.org/profile", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockBundle));

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        await _generatorService.Received(1).GenerateIpsAsync(
            "patient-123",
            "http://example.org/profile",
            Arg.Any<CancellationToken>());

        _strategyRegistry.Received(1).GetStrategy("http://example.org/profile");
    }

    [Fact]
    public async Task GivenValidIdentifier_WhenHandling_ThenCallsGeneratorServiceWithIdentifier()
    {
        // Arrange
        var query = new IpsGeneratorQuery(
            PatientId: null,
            PatientIdentifier: "http://system|12345",
            Profile: null);

        var mockBundle = CreateMockBundle();
        _generatorService.GenerateIpsByIdentifierAsync(
            "http://system",
            "12345",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockBundle));

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        await _generatorService.Received(1).GenerateIpsByIdentifierAsync(
            "http://system",
            "12345",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>());

        result.ShouldNotBeNull();
        result.IpsBundle.ShouldBe(mockBundle);
    }

    [Fact]
    public async Task GivenIdentifierWithoutSystem_WhenHandling_ThenPassesNullSystem()
    {
        // Arrange
        var query = new IpsGeneratorQuery(
            PatientId: null,
            PatientIdentifier: "12345",
            Profile: null);

        var mockBundle = CreateMockBundle();
        _generatorService.GenerateIpsByIdentifierAsync(
            null,
            "12345",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockBundle));

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        await _generatorService.Received(1).GenerateIpsByIdentifierAsync(
            null,
            "12345",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenSuccessfulGeneration_WhenHandling_ThenReturnsMetrics()
    {
        // Arrange
        var query = new IpsGeneratorQuery(
            PatientId: "patient-123",
            PatientIdentifier: null,
            Profile: null);

        var mockBundle = CreateMockBundleWithEntries(5);
        _generatorService.GenerateIpsAsync(
            "patient-123",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockBundle));

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.Metrics.ShouldNotBeNull();
        result.Metrics.TotalResources.ShouldBe(5);
        result.Metrics.TotalDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task GivenCancellationRequested_WhenHandling_ThenPassesCancellationToken()
    {
        // Arrange
        var query = new IpsGeneratorQuery(
            PatientId: "patient-123",
            PatientIdentifier: null,
            Profile: null);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mockBundle = CreateMockBundle();
        _generatorService.GenerateIpsAsync(
            "patient-123",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            cts.Token)
            .Returns(Task.FromCanceled<BundleJsonNode>(cts.Token));

        // Act & Assert
        await Should.ThrowAsync<TaskCanceledException>(
            () => _handler.HandleAsync(query, cts.Token));
    }

    [Fact]
    public async Task GivenUnknownProfile_WhenHandling_ThenFallsBackToDefaultStrategy()
    {
        // Arrange
        _strategyRegistry.GetStrategy("http://unknown.org/profile").Returns((IIpsGenerationStrategy)null);

        var query = new IpsGeneratorQuery(
            PatientId: "patient-123",
            PatientIdentifier: null,
            Profile: "http://unknown.org/profile");

        var mockBundle = CreateMockBundle();
        _generatorService.GenerateIpsAsync(
            "patient-123",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockBundle));

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        _strategyRegistry.Received(1).GetStrategy("http://unknown.org/profile");
        _strategyRegistry.Received(1).GetDefaultStrategy();
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenNoProfile_WhenHandling_ThenUsesDefaultStrategy()
    {
        // Arrange
        var query = new IpsGeneratorQuery(
            PatientId: "patient-123",
            PatientIdentifier: null,
            Profile: null);

        var mockBundle = CreateMockBundle();
        _generatorService.GenerateIpsAsync(
            "patient-123",
            "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockBundle));

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        _strategyRegistry.DidNotReceive().GetStrategy(Arg.Any<string>());
        _strategyRegistry.Received(1).GetDefaultStrategy();
        result.ShouldNotBeNull();
    }

    private static BundleJsonNode CreateMockBundle()
    {
        return new BundleJsonNode
        {
            Id = Guid.NewGuid().ToString(),
            Type = BundleJsonNode.BundleType.Document
        };
    }

    private static BundleJsonNode CreateMockBundleWithEntries(int count)
    {
        var bundle = new BundleJsonNode
        {
            Id = Guid.NewGuid().ToString(),
            Type = BundleJsonNode.BundleType.Document
        };

        for (int i = 0; i < count; i++)
        {
            bundle.Entry.Add(new BundleComponentJsonNode
            {
                FullUrl = $"urn:uuid:{Guid.NewGuid()}"
            });
        }

        return bundle;
    }
}
