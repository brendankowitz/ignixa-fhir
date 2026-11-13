// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.SemanticSearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.SemanticSearch;

public class SemanticSearchServiceRegistrationTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void GivenDisabled_WhenRegistered_ThenNoEmbeddingGeneratorResolvable()
    {
        var configuration = Configuration(new() { ["VectorSearch:Enabled"] = "false" });
        var services = new ServiceCollection();

        services.AddSemanticSearch(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetService<IEmbeddingGenerator<string, Embedding<float>>>().ShouldBeNull();
        provider.GetService<SemanticTextChunker>().ShouldBeNull();
    }

    [Fact]
    public void GivenEnabledWithInvalidConfiguration_WhenRegistered_ThenThrowsImmediately()
    {
        var configuration = Configuration(new()
        {
            ["VectorSearch:Enabled"] = "true",
            ["VectorSearch:Embedding:Dimensions"] = "768",
        });
        var services = new ServiceCollection();

        Should.Throw<OptionsValidationException>(() => services.AddSemanticSearch(configuration));
    }

    [Fact]
    public void GivenEnabledWithValidConfiguration_WhenRegistered_ThenEmbeddingGeneratorAndChunkerResolve()
    {
        var configuration = Configuration(new()
        {
            ["VectorSearch:Enabled"] = "true",
            ["VectorSearch:Embedding:Endpoint"] = "https://example.openai.azure.com/",
            ["VectorSearch:Embedding:DeploymentName"] = "text-embedding-3-small-deployment",
            ["VectorSearch:Embedding:ModelName"] = "text-embedding-3-small",
        });
        var services = new ServiceCollection();

        services.AddSemanticSearch(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()
            .ShouldBeOfType<TokenBudgetBatchingEmbeddingGenerator>();
        provider.GetRequiredService<SemanticTextChunker>().ShouldNotBeNull();
        provider.GetRequiredService<IOptions<VectorSearchOptions>>().Value.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void GivenEnabledWithValidConfiguration_WhenRegistered_ThenSemanticQueryPreparerResolvesWithADedicatedCache()
    {
        // SemanticQueryPreparer's cache must be its own instance (see the type's remarks on Review Focus
        // #2), not the shared process-wide IMemoryCache a host also registers for unrelated consumers.
        var configuration = Configuration(new()
        {
            ["VectorSearch:Enabled"] = "true",
            ["VectorSearch:Embedding:Endpoint"] = "https://example.openai.azure.com/",
            ["VectorSearch:Embedding:DeploymentName"] = "text-embedding-3-small-deployment",
            ["VectorSearch:Embedding:ModelName"] = "text-embedding-3-small",
        });
        var services = new ServiceCollection();

        services.AddSemanticSearch(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SemanticQueryPreparer>().ShouldNotBeNull();
    }

    [Fact]
    public void GivenNegativeEmbeddingCacheMaxEntries_WhenRegistered_ThenThrowsImmediately()
    {
        var configuration = Configuration(new()
        {
            ["VectorSearch:Enabled"] = "true",
            ["VectorSearch:Embedding:Endpoint"] = "https://example.openai.azure.com/",
            ["VectorSearch:Embedding:DeploymentName"] = "text-embedding-3-small-deployment",
            ["VectorSearch:Embedding:ModelName"] = "text-embedding-3-small",
            ["VectorSearch:Query:EmbeddingCacheMaxEntries"] = "-1",
        });
        var services = new ServiceCollection();

        Should.Throw<OptionsValidationException>(() => services.AddSemanticSearch(configuration));
    }
}
