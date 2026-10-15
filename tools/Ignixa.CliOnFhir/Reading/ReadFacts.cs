// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>The resources a read returned.</summary>
/// <param name="Requested">Distinct references that were read (not counting those left out by the reference limit).</param>
/// <param name="Resources">The found resources, in request order.</param>
public sealed record ReadFacts(int Requested, IReadOnlyList<JsonNode> Resources);
