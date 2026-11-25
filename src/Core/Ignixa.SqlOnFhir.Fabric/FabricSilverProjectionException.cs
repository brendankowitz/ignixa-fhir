// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// A resource cannot be held by the Silver table's schema. The message names only the FHIR path (element names and
/// indices) and a fixed reason, never a value read from the resource, so it is safe to log or persist.
/// </summary>
public sealed class FabricSilverProjectionException : Exception
{
    internal FabricSilverProjectionException(string path, string reason)
        : base($"The resource cannot be projected to the Silver table: {path} {reason}.")
    {
        Path = path;
    }

    /// <summary>Where in the resource the projection failed, for example <c>identifier[2].assigner.identifier.type</c>.</summary>
    /// <remarks>
    /// A source property name the contract does not name appears as <c>&lt;property N&gt;</c>, its zero-based position in
    /// its object, so the path never holds resource text.
    /// </remarks>
    public string Path { get; }
}
