// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// The embedded semantic text for one semantic (<c>special</c>-typed, vector-search-config-carrying)
/// search parameter on one resource, computed on the write path and attached to
/// <see cref="ResourceWrapper.VectorIndices"/>.
/// </summary>
/// <param name="SearchParameterUrl">The canonical URL of the semantic search parameter that produced <paramref name="Chunks"/>.</param>
/// <param name="EmbeddingModelKey">
/// The embedding model key the chunks were embedded with (model name and version combined). Vectors
/// produced by two different models are not comparable by distance and must never be mixed when
/// persisted or queried.
/// </param>
/// <param name="Chunks">The ordered chunks of the parameter's extracted semantic text, each with its own embedding.</param>
public sealed record VectorIndexEntry(Uri SearchParameterUrl, string EmbeddingModelKey, IReadOnlyList<VectorChunk> Chunks);
