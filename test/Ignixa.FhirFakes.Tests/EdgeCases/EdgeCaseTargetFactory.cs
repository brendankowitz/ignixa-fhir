// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

/// <summary>
/// Builds schema-typed <see cref="MutationTarget"/>s from raw JSON so strategy targeting can be
/// exercised with real <see cref="MutationTarget.InstanceType"/> values, mirroring how the pipeline
/// enumerates targets in production.
/// </summary>
internal static class EdgeCaseTargetFactory
{
    /// <summary>Shared R4 schema provider for building typed targets and pipelines in tests.</summary>
    public static readonly IFhirSchemaProvider Schema = new R4CoreSchemaProvider();

    /// <summary>Enumerates all targets for a resource described by <paramref name="json"/>.</summary>
    public static IReadOnlyList<MutationTarget> EnumerateAll(string json)
    {
        var resource = ResourceJsonNode.Parse(json);
        return ElementTreeEnumerator.Enumerate(resource, Schema);
    }

    /// <summary>Returns the single target at <paramref name="path"/> (the schema-typed location).</summary>
    public static MutationTarget AtPath(string json, string path)
    {
        var all = EnumerateAll(json);
        var target = all.FirstOrDefault(t => t.Path == path);
        return target ?? throw new InvalidOperationException(
            $"No target found at path '{path}'. Available: {string.Join(", ", all.Select(t => t.Path))}");
    }
}
