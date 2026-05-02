// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Ignixa.DeId.Models;
using Ignixa.DeId.Tests.Fixtures;
using Ignixa.DeId.Tests.Utilities;
using Ignixa.DeId.Tests.Utilities.Processors;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.DeId.Tests;

[Collection("DeId Engine Collection")]
public class DeIdEngineTests
{
    private readonly DeIdEngineFixture _fixture;

    public DeIdEngineTests(DeIdEngineFixture fixture)
    {
        _fixture = fixture;
    }
    [Fact]
    public async Task GivenAPatientJson_WhenDeidentifyViaStringOverload_ThenResultIsSuccess()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldNotBeNullOrEmpty();
        result.Value.Resource.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAResourceJsonNode_WhenDeidentifyViaNodeOverload_ThenResultIsSuccess()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));
        var resource = ResourceJsonNode.Parse(json);

        // Act
        var result = await engine.DeidentifyAsync(resource);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldNotBeNullOrEmpty();
        result.Value.Resource.ShouldNotBeNull();
        result.Value.Resource.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenMultipleResources_WhenDeidentifyManyAsync_ThenAllResultsAreReturned()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json1 = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));
        var json2 = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("bundle-basic.json"));

        var resources = ToAsyncEnumerable(
            ResourceJsonNode.Parse(json1),
            ResourceJsonNode.Parse(json2));

        // Act
        var results = new List<Result<DeIdResult>>();
        await foreach (var result in engine.DeidentifyManyAsync(resources))
        {
            results.Add(result);
        }

        // Assert
        results.Count.ShouldBe(2);
        results[0].IsSuccess.ShouldBeTrue();
        results[1].IsSuccess.ShouldBeTrue();
        results[0].Value.Resource.ResourceType.ShouldBe("Patient");
        results[1].Value.Resource.ResourceType.ShouldBe("Bundle");
    }

    [Fact]
    public async Task GivenInvalidJson_WhenDeidentify_ThenResultIsFailure()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;

        // Act
        var result = await engine.DeidentifyAsync("not valid json {{{");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("PARSE_ERROR");
    }

    [Fact]
    public async Task GivenPrettyOutputEnabled_WhenDeidentify_ThenJsonIsIndented()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));
        var settings = new RequestOptions { IsPrettyOutput = true };

        // Act
        var result = await engine.DeidentifyAsync(json, settings);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldContain("\n");
    }

    [Fact]
    public async Task GivenDeId_WhenComplete_ThenSecurityTagsAreInserted()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var outputNode = JsonNode.Parse(result.Value.DeidentifiedJson);
        var security = outputNode?["meta"]?["security"];
        security.ShouldNotBeNull();
        security.AsArray().Count.ShouldBeGreaterThan(0);

        var codes = security.AsArray()
            .Select(s => s?["code"]?.GetValue<string>())
            .ToList();
        codes.ShouldContain("REDACTED");
    }

    [Fact]
    public async Task GivenDeId_WhenComplete_ThenMetaIsInsertedAfterIdProperty()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var outputNode = JsonNode.Parse(result.Value.DeidentifiedJson)?.AsObject();
        outputNode.ShouldNotBeNull();

        var keys = outputNode.Select(kvp => kvp.Key).ToList();
        var resourceTypeIdx = keys.IndexOf("resourceType");
        var metaIdx = keys.IndexOf("meta");
        resourceTypeIdx.ShouldBeGreaterThanOrEqualTo(0);
        metaIdx.ShouldBeGreaterThan(resourceTypeIdx);
    }

    [Fact]
    public async Task GivenDeIdWithRedact_WhenComplete_ThenAppliedLabelsTrackOperations()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AppliedLabels.IsRedacted.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenDeIdWithCryptoHash_WhenComplete_ThenCryptoHashLabelIsSet()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AppliedLabels.IsCryptoHashed.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenRedactAllConfig_WhenDeidentify_ThenEmptyStructuresAreCleanedUp()
    {
        // Arrange
        var engine = _fixture.R4RedactAllEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var outputNode = JsonNode.Parse(result.Value.DeidentifiedJson);
        outputNode.ShouldNotBeNull();
        outputNode["resourceType"]?.GetValue<string>().ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenCustomProcessorRegistered_WhenDeidentify_ThenCustomProcessorIsApplied()
    {
        // Arrange
        var engine = DeIdTestHelpers.CreateR4Engine(
            DeIdTestHelpers.ConfigPath("custom-processor-config.json"),
            configure: builder => builder.AddProcessor<UpperCaseProcessor>("uppercase"));
        var json = """{"resourceType":"Patient","name":[{"given":["john"]}]}""";

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldContain("JOHN");
    }

    [Fact]
    public async Task GivenUnsupportedMethod_WhenDeidentify_ThenWarningIsEmitted()
    {
        // Arrange
        var engine = DeIdTestHelpers.CreateR4Engine(
            DeIdTestHelpers.ConfigPath("unsupported-method-config.json"));
        var json = """{"resourceType":"Patient","name":[{"given":["john"]}]}""";

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.ShouldNotBeEmpty();
        result.Value.Warnings.ShouldContain(w => w.Contains("NONEXISTENTMETHOD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GivenCompactOutputDefault_WhenDeidentify_ThenJsonHasNoNewlines()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldNotContain("\n");
    }

    [Fact]
    public async Task GivenMultipleOperationTypes_WhenDeidentify_ThenMetricsTrackEachType()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.DeidentifyAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Metrics.OperationCounts.ShouldNotBeEmpty();
        result.Value.Metrics.OperationCounts.Count.ShouldBeGreaterThan(0);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }
}
