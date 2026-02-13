// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Tests.Fixtures;
using Ignixa.Anonymizer.Tests.Utilities;
using Ignixa.Anonymizer.Tests.Utilities.Processors;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Anonymizer.Tests;

[Collection("Anonymizer Engine Collection")]
public class AnonymizerEngineTests
{
    private readonly AnonymizerEngineFixture _fixture;

    public AnonymizerEngineTests(AnonymizerEngineFixture fixture)
    {
        _fixture = fixture;
    }
    [Fact]
    public async Task GivenAPatientJson_WhenAnonymizeViaStringOverload_ThenResultIsSuccess()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
        result.Value.Resource.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAResourceJsonNode_WhenAnonymizeViaNodeOverload_ThenResultIsSuccess()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));
        var resource = ResourceJsonNode.Parse(json);

        // Act
        var result = await engine.AnonymizeAsync(resource);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
        result.Value.Resource.ShouldNotBeNull();
        result.Value.Resource.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenMultipleResources_WhenAnonymizeManyAsync_ThenAllResultsAreReturned()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json1 = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));
        var json2 = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("bundle-basic.json"));

        var resources = ToAsyncEnumerable(
            ResourceJsonNode.Parse(json1),
            ResourceJsonNode.Parse(json2));

        // Act
        var results = new List<Result<AnonymizationResult>>();
        await foreach (var result in engine.AnonymizeManyAsync(resources))
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
    public async Task GivenInvalidJson_WhenAnonymize_ThenResultIsFailure()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;

        // Act
        var result = await engine.AnonymizeAsync("not valid json {{{");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("PARSE_ERROR");
    }

    [Fact]
    public async Task GivenPrettyOutputEnabled_WhenAnonymize_ThenJsonIsIndented()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));
        var settings = new RequestOptions { IsPrettyOutput = true };

        // Act
        var result = await engine.AnonymizeAsync(json, settings);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldContain("\n");
    }

    [Fact]
    public async Task GivenAnonymization_WhenComplete_ThenSecurityTagsAreInserted()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var outputNode = JsonNode.Parse(result.Value.AnonymizedJson);
        var security = outputNode?["meta"]?["security"];
        security.ShouldNotBeNull();
        security.AsArray().Count.ShouldBeGreaterThan(0);

        var codes = security.AsArray()
            .Select(s => s?["code"]?.GetValue<string>())
            .ToList();
        codes.ShouldContain("REDACTED");
    }

    [Fact]
    public async Task GivenAnonymization_WhenComplete_ThenMetaIsInsertedAfterIdProperty()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var outputNode = JsonNode.Parse(result.Value.AnonymizedJson)?.AsObject();
        outputNode.ShouldNotBeNull();

        var keys = outputNode.Select(kvp => kvp.Key).ToList();
        var resourceTypeIdx = keys.IndexOf("resourceType");
        var metaIdx = keys.IndexOf("meta");
        resourceTypeIdx.ShouldBeGreaterThanOrEqualTo(0);
        metaIdx.ShouldBeGreaterThan(resourceTypeIdx);
    }

    [Fact]
    public async Task GivenAnonymizationWithRedact_WhenComplete_ThenAppliedLabelsTrackOperations()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AppliedLabels.IsRedacted.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAnonymizationWithCryptoHash_WhenComplete_ThenCryptoHashLabelIsSet()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AppliedLabels.IsCryptoHashed.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenRedactAllConfig_WhenAnonymize_ThenEmptyStructuresAreCleanedUp()
    {
        // Arrange
        var engine = _fixture.R4RedactAllEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var outputNode = JsonNode.Parse(result.Value.AnonymizedJson);
        outputNode.ShouldNotBeNull();
        outputNode["resourceType"]?.GetValue<string>().ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenCustomProcessorRegistered_WhenAnonymize_ThenCustomProcessorIsApplied()
    {
        // Arrange
        var engine = AnonymizerTestHelpers.CreateR4Engine(
            AnonymizerTestHelpers.ConfigPath("custom-processor-config.json"),
            configure: builder => builder.AddProcessor<UpperCaseProcessor>("uppercase"));
        var json = """{"resourceType":"Patient","name":[{"given":["john"]}]}""";

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldContain("JOHN");
    }

    [Fact]
    public async Task GivenUnsupportedMethod_WhenAnonymize_ThenWarningIsEmitted()
    {
        // Arrange
        var engine = AnonymizerTestHelpers.CreateR4Engine(
            AnonymizerTestHelpers.ConfigPath("unsupported-method-config.json"));
        var json = """{"resourceType":"Patient","name":[{"given":["john"]}]}""";

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.ShouldNotBeEmpty();
        result.Value.Warnings.ShouldContain(w => w.Contains("NONEXISTENTMETHOD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GivenCompactOutputDefault_WhenAnonymize_ThenJsonHasNoNewlines()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.AnonymizeAsync(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotContain("\n");
    }

    [Fact]
    public async Task GivenMultipleOperationTypes_WhenAnonymize_ThenMetricsTrackEachType()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;
        var json = await File.ReadAllTextAsync(AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Act
        var result = await engine.AnonymizeAsync(json);

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
