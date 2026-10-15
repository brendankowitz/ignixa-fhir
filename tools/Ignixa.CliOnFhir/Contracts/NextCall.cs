// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// A suggested follow-up call with concrete arguments. Built only from the response's structure (node keys, ids, paging
/// offsets, resource type names), never from resource content, so folder data cannot steer which tool an agent calls next.
/// </summary>
/// <param name="Tool">The MCP tool name, for example <c>context_expand</c>; the CLI verb of the same tool takes the same arguments.</param>
/// <param name="Args">The tool's arguments by parameter name; values are strings or integers.</param>
public sealed record NextCall(string Tool, IReadOnlyDictionary<string, object> Args);
