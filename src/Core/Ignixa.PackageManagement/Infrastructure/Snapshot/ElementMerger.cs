// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.PackageManagement.Infrastructure.Snapshot;

/// <summary>
/// Merges a base StructureDefinition snapshot element list with a profile differential to
/// produce the profile's snapshot element list. Pure function over raw FHIR
/// <c>ElementDefinition</c> JSON — no object model, no I/O.
/// <para>
/// M1 semantics (constraint tightening, no slicing expansion):
/// </para>
/// <list type="bullet">
/// <item>Base elements are indexed by <c>(path, sliceName)</c> and preserved in base order.</item>
/// <item>A differential element that matches a base element is merged onto it field-by-field:
/// every property present in the differential overrides the base value (<c>min</c>, <c>max</c>,
/// <c>type</c>, <c>binding</c>, <c>fixed[x]</c>, <c>pattern[x]</c>, <c>short</c>,
/// <c>definition</c>, <c>mustSupport</c>, …). <c>constraint</c> is the one additive field:
/// base invariants are kept and differential invariants are added (differential wins on a
/// shared key).</item>
/// <item>A differential element with no matching base element is inserted after its parent's
/// subtree so descendants stay contiguous. New named slices / sliced extensions are inserted
/// as-is but their base children are <b>not</b> expanded — that is M2 (see the TODO below).</item>
/// </list>
/// </summary>
/// <remarks>
/// Reference design: <c>rh-foundation/src/snapshot/merger.rs</c> (<c>merge_elements</c>). This
/// implementation intentionally does the field-by-field override in place rather than the Rust
/// facet-by-facet validation: M1 trusts the profile author's differential and reproduces the
/// shipped snapshot (validated by the shipped-snapshot oracle). Facet-legality validation
/// (reject widening cardinality, adding types not in base, weakening a binding) is deferred; it
/// is a diagnostic concern, not required to produce a correct snapshot for well-formed IGs.
/// </remarks>
internal static class ElementMerger
{
    // M2 (slicing + extension expansion): elements are keyed by ElementDefinition.id, which carries
    // the ":sliceName" disambiguator (e.g. "Patient.extension:race", "Patient.extension:race.url").
    // A differential slice header matched by id merges onto the base sliced element (carrying the
    // slicing discriminator metadata); named slice members and their sub-element subtrees are new
    // ids and are inserted adjacent to their slice group so the sibling block stays contiguous.
    // Mirrors rh-foundation ElementMerger slice handling; consumed by slicing-discriminators.md.

    private const string ConstraintProperty = "constraint";
    private const string PathProperty = "path";
    private const string SliceNameProperty = "sliceName";
    private const string SlicingProperty = "slicing";
    private const string IdProperty = "id";
    private const string KeyProperty = "key";

    /// <summary>
    /// Merges <paramref name="differentialElements"/> onto <paramref name="baseElements"/>,
    /// returning a fresh, parentless snapshot element array.
    /// </summary>
    /// <param name="baseElements">The resolved base snapshot's <c>element</c> array.</param>
    /// <param name="differentialElements">The profile's <c>differential.element</c> array.</param>
    /// <returns>The merged snapshot <c>element</c> array.</returns>
    public static JsonArray Merge(JsonArray baseElements, JsonArray differentialElements)
    {
        ArgumentNullException.ThrowIfNull(baseElements);
        ArgumentNullException.ThrowIfNull(differentialElements);

        var working = new List<JsonObject>(baseElements.Count);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in baseElements)
        {
            if (node is not JsonObject baseElement)
            {
                continue;
            }

            var clone = baseElement.DeepClone().AsObject();
            index[KeyOf(clone)] = working.Count;
            working.Add(clone);
        }

        foreach (var node in differentialElements)
        {
            if (node is not JsonObject diffElement)
            {
                continue;
            }

            var key = KeyOf(diffElement);
            if (index.TryGetValue(key, out var position))
            {
                MergeInto(working[position], diffElement);
            }
            else
            {
                InsertNewElement(working, index, diffElement);
            }
        }

        SynthesizeExtensionSlicing(working);

        var result = new JsonArray();
        foreach (var element in working)
        {
            result.Add(element);
        }

        return result;
    }

    /// <summary>
    /// The merge key for an element: its <c>id</c> when present (which carries the
    /// <c>:sliceName</c> disambiguator), otherwise a synthesized <c>path[:sliceName]</c>. For
    /// non-sliced elements <c>id</c> equals <c>path</c>, so base elements projected without an
    /// <c>id</c> and differential elements carrying one still key identically.
    /// </summary>
    private static string KeyOf(JsonObject element)
    {
        var id = SnapshotJson.GetString(element, IdProperty);
        if (!string.IsNullOrEmpty(id))
        {
            return id;
        }

        var path = SnapshotJson.GetString(element, PathProperty) ?? string.Empty;
        var slice = SnapshotJson.GetString(element, SliceNameProperty);
        return slice is null ? path : path + ":" + slice;
    }

    /// <summary>
    /// Applies every differential property onto <paramref name="target"/> in place. All fields
    /// override except <c>constraint</c>, which is unioned.
    /// </summary>
    private static void MergeInto(JsonObject target, JsonObject diff)
    {
        foreach (var property in diff)
        {
            if (property.Key == ConstraintProperty && property.Value is JsonArray diffConstraints)
            {
                target[ConstraintProperty] = MergeConstraints(target[ConstraintProperty] as JsonArray, diffConstraints);
                continue;
            }

            target[property.Key] = property.Value?.DeepClone();
        }
    }

    /// <summary>
    /// Unions base and differential invariants keyed by <c>constraint.key</c>. Base invariants
    /// are kept; a differential invariant with a new key is appended; a differential invariant
    /// re-stating an existing key replaces it (differential wins).
    /// </summary>
    private static JsonArray MergeConstraints(JsonArray? baseConstraints, JsonArray diffConstraints)
    {
        var merged = new JsonArray();
        var positionByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        if (baseConstraints is not null)
        {
            foreach (var node in baseConstraints)
            {
                if (node is not JsonObject constraint)
                {
                    continue;
                }

                var key = SnapshotJson.GetString(constraint, KeyProperty);
                merged.Add(constraint.DeepClone());
                if (key is not null)
                {
                    positionByKey[key] = merged.Count - 1;
                }
            }
        }

        foreach (var node in diffConstraints)
        {
            if (node is not JsonObject constraint)
            {
                continue;
            }

            var key = SnapshotJson.GetString(constraint, KeyProperty);
            if (key is not null && positionByKey.TryGetValue(key, out var position))
            {
                merged[position] = constraint.DeepClone();
            }
            else
            {
                merged.Add(constraint.DeepClone());
                if (key is not null)
                {
                    positionByKey[key] = merged.Count - 1;
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Inserts a differential element that has no base match, positioned immediately after the
    /// last element belonging to its parent's subtree so the flat list stays tree-walkable.
    /// </summary>
    private static void InsertNewElement(
        List<JsonObject> working,
        Dictionary<string, int> index,
        JsonObject diffElement)
    {
        var clone = diffElement.DeepClone().AsObject();
        var path = SnapshotJson.GetString(clone, PathProperty) ?? string.Empty;

        // Scope selection: a slice member (its path already exists in the working list — a same-path
        // slice header/sibling) anchors to that sliced element's own block so slices stay adjacent
        // to their header. Otherwise (a genuinely new path — a slice sub-element or a new element)
        // anchor to the parent's subtree, preserving M1 positioning.
        var sharesPathWithExisting = working.Any(w => SnapshotJson.GetString(w, PathProperty) == path);
        var scope = sharesPathWithExisting ? path : SnapshotJson.ParentPath(path);

        var insertAt = working.Count;
        if (scope.Length > 0)
        {
            var subtreePrefix = scope + ".";
            for (var i = 0; i < working.Count; i++)
            {
                var candidate = SnapshotJson.GetString(working[i], PathProperty);
                if (candidate == scope || (candidate is not null && candidate.StartsWith(subtreePrefix, StringComparison.Ordinal)))
                {
                    insertAt = i + 1;
                }
            }
        }

        working.Insert(insertAt, clone);
        Reindex(working, index);
    }

    /// <summary>
    /// Ensures every sliced <c>extension</c> / <c>modifierExtension</c> element carries a slicing
    /// header. FHIR extension slicing is implicit: an IG differential routinely lists extension
    /// slice members (<c>extension:race</c>, …) without restating the <c>value:url</c> slicing on the
    /// header (it appears only in the IG's shipped snapshot). This reproduces that behaviour so the
    /// generated snapshot carries the discriminators the validator's slicing check needs. Rules are
    /// left <c>open</c> — never synthesizing a closed slicing — so this can only enforce per-slice
    /// cardinality, never falsely reject an unknown extension.
    /// </summary>
    private static void SynthesizeExtensionSlicing(List<JsonObject> working)
    {
        var slicedExtensionPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in working)
        {
            if (SnapshotJson.GetString(element, SliceNameProperty) is null)
            {
                continue;
            }

            var path = SnapshotJson.GetString(element, PathProperty);
            if (path is not null && IsExtensionPath(path))
            {
                slicedExtensionPaths.Add(path);
            }
        }

        foreach (var path in slicedExtensionPaths)
        {
            var headerIndex = working.FindIndex(e =>
                SnapshotJson.GetString(e, PathProperty) == path && SnapshotJson.GetString(e, SliceNameProperty) is null);

            if (headerIndex >= 0)
            {
                if (working[headerIndex][SlicingProperty] is null)
                {
                    working[headerIndex][SlicingProperty] = DefaultExtensionSlicing();
                }
            }
            else
            {
                var firstSlice = working.FindIndex(e => SnapshotJson.GetString(e, PathProperty) == path);
                working.Insert(firstSlice, new JsonObject
                {
                    [PathProperty] = path,
                    ["min"] = 0,
                    ["max"] = "*",
                    [SlicingProperty] = DefaultExtensionSlicing(),
                });
            }
        }
    }

    private static bool IsExtensionPath(string path)
        => path.EndsWith(".extension", StringComparison.Ordinal)
            || path.EndsWith(".modifierExtension", StringComparison.Ordinal);

    private static JsonObject DefaultExtensionSlicing() => new()
    {
        ["discriminator"] = new JsonArray(new JsonObject { ["type"] = "value", ["path"] = "url" }),
        ["ordered"] = false,
        ["rules"] = "open",
    };

    private static void Reindex(List<JsonObject> working, Dictionary<string, int> index)
    {
        index.Clear();
        for (var i = 0; i < working.Count; i++)
        {
            index[KeyOf(working[i])] = i;
        }
    }
}
