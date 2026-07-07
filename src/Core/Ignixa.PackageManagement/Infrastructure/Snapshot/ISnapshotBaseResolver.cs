// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.PackageManagement.Infrastructure.Snapshot;

/// <summary>
/// Resolves a <c>baseDefinition</c> canonical URL to the raw StructureDefinition JSON that
/// <see cref="SnapshotGenerator"/> needs while walking the base chain.
/// <para>
/// The returned object is a full StructureDefinition node. It may itself carry a
/// <c>snapshot</c> (terminating the recursion) or only a <c>differential</c> +
/// <c>baseDefinition</c> (profile-on-profile — the generator recurses again). Implementations
/// must return a read-only view: the generator never mutates the resolved node, it deep-clones
/// the elements it consumes.
/// </para>
/// </summary>
public interface ISnapshotBaseResolver
{
    /// <summary>
    /// Resolves a canonical URL (with or without a trailing <c>|version</c>) to its
    /// StructureDefinition JSON, or <c>null</c> when the base cannot be located.
    /// </summary>
    /// <param name="canonicalUrl">The <c>baseDefinition</c> canonical URL to resolve.</param>
    /// <returns>The base StructureDefinition as a <see cref="JsonObject"/>, or <c>null</c>.</returns>
    JsonObject? ResolveStructureDefinition(string canonicalUrl);
}
