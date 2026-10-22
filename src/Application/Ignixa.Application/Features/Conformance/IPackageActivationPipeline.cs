// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Activates installed packages through the event-sourced conformance pipeline.
/// </summary>
public interface IPackageActivationPipeline
{
    /// <summary>
    /// Activates only the given package. Validation and concurrency rejections are returned as
    /// <c>ActivationResult.Failed</c> rather than thrown. The operation is idempotent for an
    /// already-active package; cancellation and infrastructure faults throw.
    /// </summary>
    Task<ActivationResult> ActivateAsync(string packageId, string version, CancellationToken cancellationToken);
}
