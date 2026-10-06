// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// Configuration options for semantic (vector) search, bound from the "VectorSearch" configuration
/// section. See <c>docs/features/semantic-search/adr-2610-semantic-vector-search.md</c>.
/// </summary>
public class VectorSearchOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "VectorSearch";

    /// <summary>
    /// The only embedding dimensionality this slice supports. Both the SQL schema's <c>vector(1536)</c>
    /// column and the provider request are hard-coded to this value; a different
    /// <see cref="VectorSearchEmbeddingOptions.Dimensions"/> fails validation rather than silently
    /// truncating or padding vectors.
    /// </summary>
    public const int SupportedDimensions = 1536;

    /// <summary>
    /// The embedding provider's per-input token limit. A chunk, or the capped prefix of an un-chunked
    /// input, that exceeds this many tokens cannot be submitted to the provider.
    /// </summary>
    public const int MaxEmbeddingInputTokens = 8192;

    /// <summary>
    /// The smallest chunk window this slice accepts. A smaller window risks the per-chunk overhead
    /// (and provider round-trips) dominating the useful signal in each chunk.
    /// </summary>
    public const int MinimumChunkSizeTokens = 16;

    /// <summary>
    /// Master switch for semantic search. When false, no embedding generator is registered and the
    /// <c>special</c> search parameters carrying a vector-search-config extension behave as unknown.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Embedding provider configuration.
    /// </summary>
    public VectorSearchEmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// Write-path (indexing) configuration.
    /// </summary>
    public VectorSearchIndexingOptions Indexing { get; set; } = new();

    /// <summary>
    /// Query-path configuration.
    /// </summary>
    public VectorSearchQueryOptions Query { get; set; } = new();

    /// <summary>
    /// The key under which vectors produced with the configured embedding model are persisted and
    /// looked up (<c>dbo.EmbeddingModel.ModelKey</c>). Combining name and version keeps vectors from two
    /// model versions -- which are not comparable by distance -- from being treated as interchangeable.
    /// </summary>
    public string EmbeddingModelKey => $"{Embedding.ModelName}|{Embedding.ModelVersion}";
}

/// <summary>
/// Embedding provider configuration for semantic search.
/// </summary>
public class VectorSearchEmbeddingOptions
{
    /// <summary>
    /// The Azure OpenAI resource endpoint. Required when <see cref="VectorSearchOptions.Enabled"/> is true.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// The Azure OpenAI deployment name to embed with. Required when <see cref="VectorSearchOptions.Enabled"/> is true.
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// The underlying model name (e.g. "text-embedding-3-small"), also used to select the tokenizer
    /// that chunks text for this model. Required when <see cref="VectorSearchOptions.Enabled"/> is true.
    /// </summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// The model version, combined with <see cref="ModelName"/> into <see cref="VectorSearchOptions.EmbeddingModelKey"/>.
    /// </summary>
    public string? ModelVersion { get; set; }

    /// <summary>
    /// Requested embedding dimensionality. Must equal <see cref="VectorSearchOptions.SupportedDimensions"/>.
    /// </summary>
    public int Dimensions { get; set; } = VectorSearchOptions.SupportedDimensions;
}

/// <summary>
/// Write-path (indexing) configuration for semantic search.
/// </summary>
public class VectorSearchIndexingOptions
{
    /// <summary>
    /// Whether semantic text is embedded synchronously on the write path or deferred to a background
    /// process. Only <see cref="VectorIndexingMode.Synchronous"/> is supported in this slice.
    /// </summary>
    public VectorIndexingMode Mode { get; set; } = VectorIndexingMode.Synchronous;

    /// <summary>
    /// The target chunk window, in tokens, used when splitting semantic text for embedding.
    /// </summary>
    public int ChunkSizeTokens { get; set; } = 800;

    /// <summary>
    /// The number of trailing tokens from one chunk repeated at the start of the next, so a passage
    /// that straddles a chunk boundary is not evenly split into fragments with no shared context.
    /// </summary>
    public int ChunkOverlapTokens { get; set; } = 100;
}

/// <summary>
/// Query-path configuration for semantic search.
/// </summary>
public class VectorSearchQueryOptions
{
    /// <summary>
    /// The vector distance metric used for ranking. Only "cosine" is supported in this slice.
    /// </summary>
    public string DistanceMetric { get; set; } = "cosine";

    /// <summary>
    /// How long a query text's embedding is cached, so repeated or paged requests for the same query
    /// text re-use one embedding instead of re-embedding (and risking a different vector) per page.
    /// </summary>
    public int EmbeddingCacheMinutes { get; set; } = 10;
}

/// <summary>
/// How semantic text is embedded on the write path.
/// </summary>
public enum VectorIndexingMode
{
    /// <summary>
    /// Semantic text is embedded synchronously as part of the write (create/update/bundle commit).
    /// </summary>
    Synchronous,

    /// <summary>
    /// Semantic text is embedded by a background process after the write commits. Not yet supported.
    /// </summary>
    Asynchronous,
}
