// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Activates an installed package through the event-sourced conformance pipeline.
/// </summary>
public interface IPackageActivationPipeline
{
    Task<ActivationResult> ActivateAsync(string packageId, string version, CancellationToken cancellationToken);
}
