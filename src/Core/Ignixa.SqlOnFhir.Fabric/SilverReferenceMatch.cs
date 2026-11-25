// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>What a Silver reference pattern recognised in a <c>Reference.reference</c> string.</summary>
/// <param name="Base">The service base preceding the type (including its scheme), or null when there is none.</param>
/// <param name="Type">The resource type named by the reference.</param>
/// <param name="Id">The resource id of a literal reference; null for a logical (<c>Type?identifier=</c>) reference.</param>
/// <param name="IdentifierSystem">The system of a logical reference's identifier query, or null when it names none.</param>
/// <param name="IdentifierValue">The value of a logical reference's identifier query.</param>
internal sealed record SilverReferenceMatch(
    string? Base,
    string Type,
    string? Id,
    string? IdentifierSystem,
    string? IdentifierValue)
{
    public bool IsLogical => Id is null;
}
