// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// A tenant hostname configuration problem: a typed <see cref="HostnameProblemKind"/> paired with a
/// human-readable message for logging.
/// </summary>
public sealed record HostnameProblem(HostnameProblemKind Kind, string Message);
