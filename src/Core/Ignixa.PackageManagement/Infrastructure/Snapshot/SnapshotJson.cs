// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.PackageManagement.Infrastructure.Snapshot;

/// <summary>
/// Small read helpers over <see cref="JsonObject"/> shared by the snapshot generation
/// components. Kept deliberately minimal: the snapshot pipeline manipulates raw FHIR
/// <c>ElementDefinition</c> JSON directly (no object model), so a handful of null-safe
/// accessors avoids repeating the same guarded casts across the merger, generator, and
/// resolver.
/// </summary>
internal static class SnapshotJson
{
    /// <summary>Reads a string-valued property, or <c>null</c> when absent or non-string.</summary>
    public static string? GetString(JsonObject obj, string property)
        => obj.TryGetPropertyValue(property, out var node) && node is JsonValue value && value.TryGetValue<string>(out var s)
            ? s
            : null;

    /// <summary>Reads an array-valued property, or <c>null</c> when absent or non-array.</summary>
    public static JsonArray? GetArray(JsonObject obj, string property)
        => obj.TryGetPropertyValue(property, out var node) ? node as JsonArray : null;

    /// <summary>Reads an object-valued property, or <c>null</c> when absent or non-object.</summary>
    public static JsonObject? GetObject(JsonObject obj, string property)
        => obj.TryGetPropertyValue(property, out var node) ? node as JsonObject : null;

    /// <summary>
    /// Deep-clones every <see cref="JsonObject"/> in <paramref name="array"/> into a fresh,
    /// parentless <see cref="JsonArray"/>. Non-object entries are skipped.
    /// </summary>
    public static JsonArray CloneElements(JsonArray array)
    {
        var result = new JsonArray();
        foreach (var node in array)
        {
            if (node is JsonObject obj)
            {
                result.Add(obj.DeepClone());
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the parent path of a dotted FHIR element path (everything before the last
    /// segment), or the empty string for a single-segment root path.
    /// </summary>
    public static string ParentPath(string path)
    {
        var lastDot = path.LastIndexOf('.');
        return lastDot < 0 ? string.Empty : path[..lastDot];
    }
}
