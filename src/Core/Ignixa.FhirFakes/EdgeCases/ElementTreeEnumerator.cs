// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// Enumerates the primitive-valued leaves of a resource as <see cref="MutationTarget"/>s by walking
/// the schema-typed <see cref="IElement"/> tree. Each target carries the schema-derived
/// <see cref="MutationTarget.InstanceType"/> and required-binding flag that strategies gate on, plus
/// the backing value <see cref="JsonNode"/> for in-place mutation.
/// </summary>
/// <remarks>
/// This replaces the former blind raw-JsonNode walk + element-name/regex heuristics. The schema
/// already knows each element's exact FHIR type and binding, so targeting is precise: a bound
/// <c>code</c> is never offered to a free-text strategy, and only true <c>date</c>/<c>dateTime</c>
/// leaves are offered to temporal strategies. Mutation goes through <see cref="IElement.Meta{T}"/>
/// to reach the backing value node, then the node's parent (the proven <c>JsonNodeMutator</c>
/// technique). Walk order is depth-first pre-order over <see cref="IElement.Children()"/>, which is
/// stable for a given input, preserving determinism.
/// </remarks>
public static class ElementTreeEnumerator
{
    /// <summary>Walks the resource and returns every mutable primitive string leaf, in stable order.</summary>
    public static IReadOnlyList<MutationTarget> Enumerate(ResourceJsonNode resource, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var targets = new List<MutationTarget>();
        WalkChildren(resource.ToElement(schemaProvider), targets);
        return targets;
    }

    private static void WalkChildren(IElement element, List<MutationTarget> targets)
    {
        foreach (var child in element.Children())
        {
            if (child.HasPrimitiveValue)
            {
                AddLeafIfStringValued(child, targets);
            }

            WalkChildren(child, targets);
        }
    }

    private static void AddLeafIfStringValued(IElement leaf, List<MutationTarget> targets)
    {
        if (leaf.Value is not string value)
        {
            return;
        }

        var valueNode = ResolveValueNode(leaf);
        if (valueNode is null)
        {
            throw new InvalidOperationException(
                $"Leaf '{leaf.Location}' (InstanceType '{leaf.InstanceType}') reports a primitive string value but no backing JsonValue node could be resolved — ElementTreeEnumerator invariant violated.");
        }

        targets.Add(new MutationTarget(
            valueNode,
            leaf.Name,
            leaf.Location,
            value,
            leaf.InstanceType,
            IsRequiredBound(leaf.Type)));
    }

    // When a primitive carries both a value and a "_value" shadow object, Meta<JsonNode>() returns
    // the shadow; Meta<JsonPrimitiveValueNode>() reaches the real value node. Prefer it, then fall
    // back to Meta<JsonNode>() for the common value-only case.
    private static JsonNode? ResolveValueNode(IElement leaf)
    {
        var primitive = leaf.Meta<JsonPrimitiveValueNode>();
        if (primitive is not null)
        {
            return primitive.Value;
        }

        return leaf.Meta<JsonNode>() is JsonValue valueNode ? valueNode : null;
    }

    // internal (not private) so the PascalCase casing fix can be unit-tested directly against the
    // real generated schema binding ("Required"), independent of which leaves the enumerator happens
    // to yield. See ElementTreeEnumeratorTests.
    internal static bool IsRequiredBound(IType? type)
        => type is ITypeExtended { Binding.Strength: { } strength }
            && string.Equals(strength, "required", StringComparison.OrdinalIgnoreCase);
}
