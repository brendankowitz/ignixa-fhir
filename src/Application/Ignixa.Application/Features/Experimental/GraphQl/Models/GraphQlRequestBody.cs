// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.Application.Features.Experimental.GraphQl.Models;

public sealed record GraphQlRequestBody(string? Query, string? OperationName, JsonElement? Variables);
