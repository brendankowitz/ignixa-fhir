// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Operations.Features.Transform;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.Metadata.Segments;

/// <summary>
/// Tests for OperationsSegment to verify operation exposure in CapabilityStatement.
/// </summary>
public class OperationsSegmentTests
{
    private readonly IPackageResourceRepository _packageResourceRepository;
    private readonly OperationsSegment _segment;
    private readonly List<IPackageFeature> _features;

    public OperationsSegmentTests()
    {
        _packageResourceRepository = Substitute.For<IPackageResourceRepository>();
        _features = [];

        _segment = new OperationsSegment(
            _features,
            _packageResourceRepository,
            NullLogger<OperationsSegment>.Instance);
    }

    [Fact]
    public async Task GivenTransformFeature_WhenApplyingSegment_ThenAddsTransformOperation()
    {
        // Arrange
        var transformFeature = new StructureMapTransformFeature();
        _features.Add(transformFeature);

        var mockSegment = new OperationsSegment(
            _features,
            _packageResourceRepository,
            NullLogger<OperationsSegment>.Instance);

        var statement = new CapabilityStatementJsonNode();
        var context = new CapabilityContext(
            FhirVersion: FhirVersion.R4,
            TenantId: 1);

        // Setup repository to return a mock OperationDefinition for "transform"
        var transformOpDef = new PackageResource
        {
            ResourceId = "transform",
            ResourceType = "OperationDefinition",
            Canonical = "http://hl7.org/fhir/OperationDefinition/StructureMap-transform",
            PackageId = "hl7.fhir.core",
            PackageVersion = "1.0.0",
            FhirVersion = "R4",
            ResourceJson = "{}"
        };

        _packageResourceRepository
            .GetOperationDefinitionsAsync(
                Arg.Is<List<string>>(list => list.Contains("transform")),
                "R4",
                Arg.Any<CancellationToken>())
            .Returns([transformOpDef]);

        // Act
        await mockSegment.ApplyAsync(statement, context, CancellationToken.None);

        // Assert
        var rest = statement.Rest;
        rest.ShouldNotBeNull();
        rest.Count.ShouldBeGreaterThan(0);

        var restComponent = rest![0];
        var resources = restComponent.Resource;
        resources.ShouldNotBeNull();

        var structureMapResource = resources!.FirstOrDefault(r => r.Type == "StructureMap");
        structureMapResource.ShouldNotBeNull("StructureMap resource should be in CapabilityStatement");

        // Check for operations on the resource component
        var operationsArray = structureMapResource!.MutableNode["operation"];
        operationsArray.ShouldNotBeNull("operation array should exist");

        var operations = operationsArray!.AsArray();
        operations.Count.ShouldBeGreaterThan(0);

        var transformOp = operations.FirstOrDefault(op =>
            op?["name"]?.GetValue<string>() == "transform");
        transformOp.ShouldNotBeNull("transform operation should be listed");
        transformOp!["definition"]?.GetValue<string>()
            .ShouldBe("http://hl7.org/fhir/OperationDefinition/StructureMap-transform");
    }

    [Fact]
    public void GivenTransformFeature_WhenCheckingResourceOperations_ThenIncludesStructureMap()
    {
        // Arrange
        var feature = new StructureMapTransformFeature();

        // Act
        var resourceOps = feature.ResourceOperations;

        // Assert
        resourceOps.ShouldContainKey("StructureMap");
        resourceOps["StructureMap"].ShouldContain("transform");
    }

    [Fact]
    public void GivenTransformFeature_WhenCheckingPackageId_ThenReturnsCorrectId()
    {
        // Arrange
        var feature = new StructureMapTransformFeature();

        // Act
        var packageId = feature.PackageId;

        // Assert
        packageId.ShouldBe("hl7.fhir.core");
    }

    [Fact]
    public void GivenTransformFeature_WhenCheckingSupportedVersions_ThenSupportsAllVersions()
    {
        // Arrange
        var feature = new StructureMapTransformFeature();

        // Act
        var versions = feature.SupportedFhirVersions;

        // Assert
        versions.ShouldBeNull("null means supports all FHIR versions");
    }

    [Fact]
    public async Task GivenGraphQlFeature_WhenApplyingSegment_ThenAddsGraphQlSystemOperation()
    {
        // Arrange
        var graphQlFeature = new GraphQlFeature();
        _features.Add(graphQlFeature);

        var segment = new OperationsSegment(
            _features,
            _packageResourceRepository,
            NullLogger<OperationsSegment>.Instance);

        var statement = new CapabilityStatementJsonNode();
        var context = new CapabilityContext(
            FhirVersion: FhirVersion.R4,
            TenantId: 1);

        // Setup repository to return a mock OperationDefinition for "graphql"
        var graphQlOpDef = new PackageResource
        {
            ResourceId = "graphql",
            ResourceType = "OperationDefinition",
            Canonical = "http://hl7.org/fhir/OperationDefinition/Resource-graphql",
            PackageId = "hl7.fhir.core",
            PackageVersion = "4.0.1",
            FhirVersion = "R4",
            ResourceJson = "{}"
        };

        _packageResourceRepository
            .GetOperationDefinitionsAsync(
                Arg.Is<List<string>>(list => list.Count == 1 && list[0] == "graphql"),
                "R4",
                Arg.Any<CancellationToken>())
            .Returns([graphQlOpDef]);

        // Act
        await segment.ApplyAsync(statement, context, CancellationToken.None);

        // Assert
        var rest = statement.Rest;
        rest.ShouldNotBeNull();
        rest.Count.ShouldBeGreaterThan(0);

        var restComponent = rest![0];
        var operationsArray = restComponent.MutableNode["operation"];
        operationsArray.ShouldNotBeNull("operation array should exist on rest component");

        var operations = operationsArray!.AsArray();
        operations.Count.ShouldBeGreaterThan(0);

        var graphQlOp = operations.FirstOrDefault(op =>
            op?["name"]?.GetValue<string>() == "graphql");
        graphQlOp.ShouldNotBeNull("graphql operation should be listed as a system operation");
        graphQlOp["definition"]?.GetValue<string>()
            .ShouldBe("http://hl7.org/fhir/OperationDefinition/Resource-graphql");
    }

    [Fact]
    public void GivenGraphQlFeature_WhenCheckingSystemOperations_ThenIncludesGraphQl()
    {
        // Arrange
        var feature = new GraphQlFeature();

        // Act
        var systemOps = feature.SystemOperations;

        // Assert
        systemOps.ShouldContain("graphql");
    }

    [Fact]
    public void GivenGraphQlFeature_WhenCheckingResourceOperations_ThenIsEmpty()
    {
        // Arrange
        var feature = new GraphQlFeature();

        // Act
        var resourceOps = feature.ResourceOperations;

        // Assert
        resourceOps.ShouldBeEmpty();
    }

    [Fact]
    public void GivenGraphQlFeature_WhenCheckingPackageId_ThenReturnsHl7FhirCore()
    {
        // Arrange
        var feature = new GraphQlFeature();

        // Act
        var packageId = feature.PackageId;

        // Assert
        packageId.ShouldBe("hl7.fhir.core");
    }

    [Fact]
    public void GivenGraphQlFeature_WhenCheckingSupportedVersions_ThenSupportsAllVersions()
    {
        // Arrange
        var feature = new GraphQlFeature();

        // Act
        var versions = feature.SupportedFhirVersions;

        // Assert
        versions.ShouldBeNull("null means supports all FHIR versions");
    }
}
