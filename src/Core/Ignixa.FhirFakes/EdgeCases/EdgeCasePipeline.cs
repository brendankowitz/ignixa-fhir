// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// Applies edge-case strategies to an already-generated resource as a seeded decorator pass.
/// Owns a dedicated <see cref="Randomizer"/> so its output is fully reproducible from the seed,
/// independent of the core generator's (currently absent) determinism.
/// </summary>
/// <remarks>
/// Determinism contract: the same seed, the same input resource, and the same ordered strategy set
/// produce byte-identical mutated JSON and an identical manifest. Targets are enumerated from the
/// schema-typed element tree in a stable order and the RNG is drawn from only for targets that have
/// an applicable strategy.
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class EdgeCasePipeline
{
    private readonly int _seed;
    private readonly Randomizer _rng;
    private readonly IFhirSchemaProvider _schemaProvider;

    /// <summary>Creates a pipeline with a dedicated seeded randomizer and the schema used to type targets.</summary>
    public EdgeCasePipeline(int seed, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        _seed = seed;
        _rng = new Randomizer(seed);
        _schemaProvider = schemaProvider;
    }

    /// <summary>
    /// Walks the resource, applies one eligible <see cref="ValidityIntent.PreservesValidity"/> strategy
    /// per matching leaf (chosen deterministically), mutates the resource in place, and returns a
    /// manifest of every change.
    /// </summary>
    public MutationManifest Apply(ResourceJsonNode resource, IReadOnlyList<IEdgeCaseStrategy> strategies)
        => Apply(resource, strategies, includeNonValidityPreserving: false);

    /// <summary>
    /// Walks the resource, applies eligible strategies per <paramref name="includeNonValidityPreserving"/>,
    /// mutates the resource in place, and returns a manifest of every change.
    /// When <paramref name="includeNonValidityPreserving"/> is <see langword="false"/> the behaviour is
    /// identical to <see cref="Apply(ResourceJsonNode, IReadOnlyList{IEdgeCaseStrategy})"/>.
    /// </summary>
    public MutationManifest Apply(
        ResourceJsonNode resource,
        IReadOnlyList<IEdgeCaseStrategy> strategies,
        bool includeNonValidityPreserving)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(strategies);

        var eligible = includeNonValidityPreserving
            ? strategies.ToList()
            : strategies.Where(s => s.Intent == ValidityIntent.PreservesValidity).ToList();

        var records = new List<MutationRecord>();

        if (eligible.Count > 0)
        {
            ApplyToTargets(resource, eligible, records);
        }

        resource.InvalidateCaches();
        return new MutationManifest(resource.Id, _seed, records);
    }

    private void ApplyToTargets(ResourceJsonNode resource, List<IEdgeCaseStrategy> eligible, List<MutationRecord> records)
    {
        foreach (var target in ElementTreeEnumerator.Enumerate(resource, _schemaProvider))
        {
            TryApplyOne(target, eligible, records);
        }
    }

    private void TryApplyOne(MutationTarget target, List<IEdgeCaseStrategy> eligible, List<MutationRecord> records)
    {
        var applicable = eligible.Where(s => s.CanApply(target)).ToList();
        if (applicable.Count == 0)
        {
            return;
        }

        var strategy = applicable[_rng.Number(0, applicable.Count - 1)];
        var before = target.Value;
        var result = strategy.Apply(target, _rng);
        target.Replace(result.NewValue);
        records.Add(new MutationRecord(strategy.Category, target.Path, before, result.NewValue, result.Description));
    }
}
