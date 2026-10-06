// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// Registers semantic (vector) search services: bound and validated <see cref="VectorSearchOptions"/>,
/// the <see cref="SemanticTextChunker"/>, the Azure OpenAI-backed
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>, and the write-path <see cref="SemanticIndexer"/>.
/// When semantic search is disabled, registers nothing -- no embedding generator is resolvable, no
/// provider call is possible, and <see cref="SemanticIndexer"/> is not constructed.
/// </summary>
public static class SemanticSearchServiceRegistration
{
    /// <summary>
    /// The keyed-service key under which the raw, provider-backed embedding generator is registered,
    /// before it is wrapped by <see cref="TokenBudgetBatchingEmbeddingGenerator"/>.
    /// </summary>
    /// <remarks>
    /// Registered under a key -- rather than directly as <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>
    /// -- so an E2E test host can replace only the live Azure OpenAI client with a deterministic
    /// generator while still exercising the real batching/chunking wrapper. A test fixture that calls
    /// <c>services.AddKeyedSingleton(ProviderGeneratorKey, ...)</c> after this method has run replaces this
    /// registration: Microsoft.Extensions.DependencyInjection resolves the last-registered implementation
    /// for a given service+key, and ASP.NET Core's <c>WebApplicationFactory.ConfigureServices</c> callback
    /// (see <c>IgnixaApiFixture.ConfigureWebHost</c>) runs after the production <c>ConfigureServices</c>
    /// that calls this method.
    /// </remarks>
    public const string ProviderGeneratorKey = "semantic-search-provider";

    /// <summary>
    /// Binds "VectorSearch" configuration, validates it when enabled, and -- only when enabled --
    /// registers the chunker and embedding generator singletons.
    /// </summary>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">
    /// VectorSearch is enabled but misconfigured. See <see cref="VectorSearchOptionsValidator"/>.
    /// </exception>
    public static IServiceCollection AddSemanticSearch(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new VectorSearchOptions();
        configuration.GetSection(VectorSearchOptions.SectionName).Bind(options);

        if (!options.Enabled)
        {
            return services;
        }

        VectorSearchOptionsValidator.Validate(options);

        services.Configure<VectorSearchOptions>(configuration.GetSection(VectorSearchOptions.SectionName));

        services.AddSingleton(_ => new SemanticTextChunker(options.Embedding.ModelName!));

        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(ProviderGeneratorKey, (sp, _) =>
        {
            // Prefer an already-registered TokenCredential (e.g. a Managed Identity credential the host
            // configured for another Azure client) over constructing a new DefaultAzureCredential, so this
            // provider authenticates the same way the rest of the deployment does.
            var credential = sp.GetService<TokenCredential>() ?? new DefaultAzureCredential();
            var client = new AzureOpenAIClient(options.Embedding.Endpoint!, credential);
            return client.GetEmbeddingClient(options.Embedding.DeploymentName).AsIEmbeddingGenerator(options.Embedding.Dimensions);
        });

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var inner = sp.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(ProviderGeneratorKey);
            var tokenizer = sp.GetRequiredService<SemanticTextChunker>();
            return new TokenBudgetBatchingEmbeddingGenerator(inner, tokenizer, options.Embedding.Dimensions);
        });

        // Write-path indexer. Handlers and DeferredWriteCoordinator take this as an optional constructor
        // dependency (see the IMcpAuthorizationService precedent for the same pattern): resolved when
        // enabled, and -- because nothing is registered here when disabled -- Autofac supplies the
        // constructor parameter's default value (null) instead of throwing, so disabled writes simply
        // skip semantic indexing rather than needing an explicit "is this enabled" check at every call site.
        services.AddSingleton(sp => new SemanticIndexer(
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            sp.GetRequiredService<SemanticTextChunker>(),
            options));

        return services;
    }
}
