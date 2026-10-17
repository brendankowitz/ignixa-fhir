// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>A draft built from its input, before its id is given: the resource, and what in it must be checked.</summary>
/// <param name="Resource">The resource, without <c>id</c>.</param>
/// <param name="Codings">Every coded value the input gave, in resource order.</param>
/// <param name="References">Every resource of the folder it refers to, the subject first.</param>
internal sealed record DraftContent(JsonObject Resource, IReadOnlyList<DraftCoding> Codings, IReadOnlyList<DraftReference> References);
