// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.SemanticSearch;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.SemanticSearch;

public class VectorSearchOptionsValidatorTests
{
    private static VectorSearchOptions ValidEnabledOptions() => new()
    {
        Enabled = true,
        Embedding = new VectorSearchEmbeddingOptions
        {
            Endpoint = new Uri("https://example.openai.azure.com/"),
            DeploymentName = "text-embedding-3-small-deployment",
            ModelName = "text-embedding-3-small",
            ModelVersion = "1",
            Dimensions = VectorSearchOptions.SupportedDimensions,
        },
    };

    [Fact]
    public void GivenDisabled_WhenValidated_ThenDoesNotThrowEvenWhenOtherwiseInvalid()
    {
        var options = new VectorSearchOptions { Enabled = false };

        Should.NotThrow(() => VectorSearchOptionsValidator.Validate(options));
    }

    [Fact]
    public void GivenValidEnabledOptions_WhenValidated_ThenDoesNotThrow()
    {
        Should.NotThrow(() => VectorSearchOptionsValidator.Validate(ValidEnabledOptions()));
    }

    [Fact]
    public void GivenEnabledWithDimensions768_WhenValidated_ThenFails()
    {
        var options = ValidEnabledOptions();
        options.Embedding.Dimensions = 768;

        var exception = Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));

        exception.Failures.ShouldContain(failure => failure.Contains("Dimensions", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenAsynchronousMode_WhenValidated_ThenFailsNotYetSupported()
    {
        var options = ValidEnabledOptions();
        options.Indexing.Mode = VectorIndexingMode.Asynchronous;

        var exception = Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));

        exception.Failures.ShouldContain("Indexing.Mode 'Asynchronous' is not yet supported.");
    }

    [Fact]
    public void GivenMissingEndpoint_WhenValidated_ThenFails()
    {
        var options = ValidEnabledOptions();
        options.Embedding.Endpoint = null;

        Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));
    }

    [Fact]
    public void GivenMissingDeploymentName_WhenValidated_ThenFails()
    {
        var options = ValidEnabledOptions();
        options.Embedding.DeploymentName = null;

        Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));
    }

    [Fact]
    public void GivenUnknownModelName_WhenValidated_ThenFails()
    {
        var options = ValidEnabledOptions();
        options.Embedding.ModelName = "not-a-real-model-xyz";

        Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));
    }

    [Fact]
    public void GivenNonCosineDistanceMetric_WhenValidated_ThenFails()
    {
        var options = ValidEnabledOptions();
        options.Query.DistanceMetric = "l2";

        Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9000)]
    public void GivenChunkSizeOutOfRange_WhenValidated_ThenFails(int chunkSizeTokens)
    {
        var options = ValidEnabledOptions();
        options.Indexing.ChunkSizeTokens = chunkSizeTokens;
        options.Indexing.ChunkOverlapTokens = 0;

        Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));
    }

    [Fact]
    public void GivenOverlapNotLessThanChunkSize_WhenValidated_ThenFails()
    {
        var options = ValidEnabledOptions();
        options.Indexing.ChunkSizeTokens = 100;
        options.Indexing.ChunkOverlapTokens = 100;

        Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));
    }

    [Fact]
    public void GivenNegativeEmbeddingCacheMinutes_WhenValidated_ThenFails()
    {
        var options = ValidEnabledOptions();
        options.Query.EmbeddingCacheMinutes = -1;

        Should.Throw<OptionsValidationException>(() => VectorSearchOptionsValidator.Validate(options));
    }
}
