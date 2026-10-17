// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>The resources a read returned.</summary>
/// <param name="Requested">Distinct references that were read (not counting those left out by the reference limit).</param>
/// <param name="Resources">The found resources, in request order, as written.</param>
/// <param name="ResolvedReferences">
/// For each returned resource (<c>Type/id</c>), the Bundle references it shows as written (<c>urn:uuid:...</c>, a fullUrl) and
/// the <c>Type/id</c> they name, as search lists them: at most 100 (the rest are counted in <c>omitted</c>); absent when the
/// returned resources show none.
/// </param>
public sealed record ReadFacts(
    int Requested,
    IReadOnlyList<JsonNode> Resources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ResolvedReferences = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AttachmentText>? Texts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<IReadOnlyList<DocumentVersion>>? Versions = null);
