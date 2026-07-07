// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.PackageManagement.Infrastructure.Snapshot;

namespace Ignixa.PackageManagement.Tests.Snapshot;

/// <summary>
/// Test <see cref="ISnapshotBaseResolver"/> that resolves a <c>baseDefinition</c> URL against a
/// fixed set of StructureDefinition JSON objects indexed by their canonical <c>url</c>. Used by
/// the shipped-snapshot oracle and unit tests to feed real base snapshots into the generator
/// without depending on a live package cache.
/// </summary>
internal sealed class FixtureBaseResolver : ISnapshotBaseResolver
{
    private readonly Dictionary<string, JsonObject> _byUrl = new(StringComparer.Ordinal);

    public FixtureBaseResolver(IEnumerable<JsonObject> structureDefinitions)
    {
        foreach (var sd in structureDefinitions)
        {
            if (sd["url"]?.GetValue<string>() is { } url)
            {
                _byUrl[url] = sd;
            }
        }
    }

    public JsonObject? ResolveStructureDefinition(string canonicalUrl)
        => _byUrl.TryGetValue(canonicalUrl, out var sd) ? sd : null;
}
