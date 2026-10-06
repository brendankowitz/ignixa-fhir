// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Ignixa.Application.Tests.SemanticSearch;

/// <summary>
/// Test-only <see cref="IEmbeddingGenerator{String, Embedding}"/> that never calls a live provider.
/// Produces a deterministic, L2-normalized 1536-dimension vector from the SHA-256 hash of the input
/// text, so the same text always embeds to the same vector and different texts embed to different
/// vectors -- enough to exercise chunking, batching, persistence and ranking without a network call.
/// </summary>
/// <remarks>
/// Lives in <c>Ignixa.Application.Tests</c> rather than a shared test-support project: no such project
/// exists yet for this feature area (<c>Ignixa.Serialization.TestSupport</c> is the closest precedent,
/// scoped to serialization). <c>test/Ignixa.Api.E2ETests</c> swaps in a deterministic generator for its
/// SQL E2E suite (Review Focus #2, paging with a non-deterministic provider) by linking this file --
/// <c>&lt;Compile Include="..\Ignixa.Application.Tests\SemanticSearch\DeterministicEmbeddingGenerator.cs" Link="..." /&gt;</c>
/// -- rather than taking a project reference to a test assembly full of unrelated xUnit facts.
/// </remarks>
public sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    /// <summary>The fixed embedding dimensionality this slice supports.</summary>
    public const int Dimensions = 1536;

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var results = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(new Embedding<float>(Generate(value)));
        }

        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// Deterministically derives a unit-length <see cref="Dimensions"/>-length vector from
    /// <paramref name="text"/>: repeated SHA-256 of the text's hash concatenated with an incrementing
    /// counter, with each hash byte mapped from [0, 255] to [-1, 1], then L2-normalized.
    /// </summary>
    public static ReadOnlyMemory<float> Generate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vector = new float[Dimensions];
        var produced = 0;
        var counter = 0;

        while (produced < Dimensions)
        {
            var block = counter == 0 ? seed : SHA256.HashData([.. seed, .. BitConverter.GetBytes(counter)]);

            for (var i = 0; i < block.Length && produced < Dimensions; i++)
            {
                vector[produced] = (block[i] / 255f * 2f) - 1f;
                produced++;
            }

            counter++;
        }

        Normalize(vector);
        return vector;
    }

    private static void Normalize(float[] vector)
    {
        var sumOfSquares = 0d;
        foreach (var component in vector)
        {
            sumOfSquares += (double)component * component;
        }

        var magnitude = Math.Sqrt(sumOfSquares);
        if (magnitude == 0)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / magnitude);
        }
    }
}
