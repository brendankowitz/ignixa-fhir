// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Terminology.Remote;

/// <summary>One RxClass answer.</summary>
/// <param name="Body">The JSON object the server answered (an empty object when it knows nothing).</param>
/// <param name="RetrievedAt">When the server sent it; for a cached answer, when it was first retrieved.</param>
/// <param name="FromCache">Whether it was read from the disk cache instead of the network.</param>
/// <param name="CacheProblem">Why the disk cache could not be read or written for it; null when it could.</param>
internal sealed record RxClassResponse(JsonObject Body, DateTimeOffset RetrievedAt, bool FromCache, string? CacheProblem);
