// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// Validates <see cref="VectorSearchOptions"/> when semantic search is enabled. Called eagerly from
/// <see cref="SemanticSearchServiceRegistration.AddSemanticSearch"/> during service registration --
/// the repository has no existing <c>ValidateOnStart</c> precedent, so this fails startup synchronously
/// instead of deferring to a hosted-service-triggered options validation pass.
/// </summary>
public static class VectorSearchOptionsValidator
{
    /// <summary>
    /// Validates <paramref name="options"/>. A no-op when <see cref="VectorSearchOptions.Enabled"/> is false.
    /// </summary>
    /// <exception cref="OptionsValidationException">One or more settings are invalid while enabled.</exception>
    public static void Validate(VectorSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return;
        }

        var failures = new List<string>();

        if (options.Embedding.Endpoint is null)
        {
            failures.Add("Embedding.Endpoint is required when VectorSearch is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Embedding.DeploymentName))
        {
            failures.Add("Embedding.DeploymentName is required when VectorSearch is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Embedding.ModelName))
        {
            failures.Add("Embedding.ModelName is required when VectorSearch is enabled.");
        }
        else
        {
            try
            {
                _ = new SemanticTextChunker(options.Embedding.ModelName);
            }
            catch (ArgumentException ex)
            {
                failures.Add($"Embedding.ModelName: {ex.Message}");
            }
        }

        if (options.Embedding.Dimensions != VectorSearchOptions.SupportedDimensions)
        {
            failures.Add(
                $"Embedding.Dimensions must be {VectorSearchOptions.SupportedDimensions}; was {options.Embedding.Dimensions}.");
        }

        if (!string.Equals(options.Query.DistanceMetric, "cosine", StringComparison.Ordinal))
        {
            failures.Add("Query.DistanceMetric must be 'cosine'.");
        }

        if (options.Query.EmbeddingCacheMinutes < 0)
        {
            failures.Add("Query.EmbeddingCacheMinutes must be 0 or greater.");
        }

        if (options.Query.EmbeddingCacheMaxEntries < 0)
        {
            failures.Add("Query.EmbeddingCacheMaxEntries must be 0 or greater.");
        }

        if (options.Indexing.ChunkSizeTokens < VectorSearchOptions.MinimumChunkSizeTokens ||
            options.Indexing.ChunkSizeTokens > VectorSearchOptions.MaxEmbeddingInputTokens)
        {
            failures.Add(
                $"Indexing.ChunkSizeTokens must be between {VectorSearchOptions.MinimumChunkSizeTokens} " +
                $"and {VectorSearchOptions.MaxEmbeddingInputTokens}.");
        }

        if (options.Indexing.ChunkOverlapTokens < 0 || options.Indexing.ChunkOverlapTokens >= options.Indexing.ChunkSizeTokens)
        {
            failures.Add("Indexing.ChunkOverlapTokens must be 0 or greater and less than Indexing.ChunkSizeTokens.");
        }

        if (options.Indexing.Mode == VectorIndexingMode.Asynchronous)
        {
            failures.Add("Indexing.Mode 'Asynchronous' is not yet supported.");
        }

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(VectorSearchOptions.SectionName, typeof(VectorSearchOptions), failures);
        }
    }
}
