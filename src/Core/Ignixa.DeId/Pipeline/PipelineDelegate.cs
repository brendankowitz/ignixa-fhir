// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Models;

namespace Ignixa.DeId.Pipeline;

/// <summary>
/// Delegate representing the next handler in the pipeline.
/// </summary>
/// <param name="context">The de-identification context.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>A result containing the de-identification result or an error.</returns>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix - Delegate is the appropriate naming here
public delegate ValueTask<Result<DeIdResult>> PipelineDelegate(
    DeIdContext context,
    CancellationToken cancellationToken);
#pragma warning restore CA1711
