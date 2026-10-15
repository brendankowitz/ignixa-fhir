// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// One embedded chunk of semantic text produced for a single semantic search parameter on one
/// resource (see <see cref="VectorIndexEntry"/>).
/// </summary>
/// <param name="Ordinal">
/// Position of this chunk among every chunk produced for the owning <see cref="VectorIndexEntry"/>,
/// contiguous from 0 across all of the parameter's extracted source texts (every value, for the
/// <c>perValueRow</c> extraction policy).
/// </param>
/// <param name="Passage">
/// The exact source text this chunk was embedded from, persisted alongside its vector for diagnostics
/// and potential re-embedding after a model change.
/// </param>
/// <param name="Embedding">The embedding vector produced for <paramref name="Passage"/>.</param>
public sealed record VectorChunk(short Ordinal, string Passage, ReadOnlyMemory<float> Embedding);
