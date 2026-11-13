// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions;

/// <summary>
/// An embedded query, ready for the query-time SQL lowering (Task 7) to bind into the ranking
/// <c>CROSS APPLY</c>: the query text's embedding, the model key it was produced with (so a cached
/// embedding is never mixed with vectors from a different model/version), and the maximum cosine
/// distance a row must be within to pass the parameter's <c>minimumScore</c> gate (see
/// <see cref="Ignixa.Search.Models.VectorSearchConfig.MinimumScore"/> and the ADR's
/// <c>distance &lt;= 2 * (1 - minimumScore)</c> gate formula).
/// </summary>
/// <param name="Embedding">The 1536-dimension embedding vector for <see cref="VectorSearchExpression.QueryText"/>.</param>
/// <param name="EmbeddingModelKey">The embedding model/version the vector was produced with.</param>
/// <param name="MaxDistance">The inclusive maximum cosine distance a candidate row's vector may have from <see cref="Embedding"/>.</param>
public sealed record PreparedVectorQuery(ReadOnlyMemory<float> Embedding, string EmbeddingModelKey, double MaxDistance);
