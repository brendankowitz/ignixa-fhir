// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.GraphQl.Directives;

/// <summary>
/// Thrown when a <c>@singleton</c> directive is applied to a list that does not contain exactly one element.
/// </summary>
public sealed class SingletonDirectiveViolationException(string message) : InvalidOperationException(message);
