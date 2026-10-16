// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// A suggested follow-up call with concrete arguments. Built only from the response's structure (node keys, ids, paging
/// offsets, resource type names), never from resource content, so folder data cannot steer which tool an agent calls next.
/// </summary>
/// <param name="Tool">The MCP tool name, for example <c>patient_context</c> expand; the CLI verb of the same tool takes the same arguments.</param>
/// <param name="Args">The tool's arguments by parameter name; values are strings, integers or lists of strings.</param>
/// <param name="Hint">When the call narrows or widens the one just made instead of moving on, what it changes and why; null otherwise.</param>
public sealed record NextCall(string Tool, IReadOnlyDictionary<string, object> Args, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hint = null);
