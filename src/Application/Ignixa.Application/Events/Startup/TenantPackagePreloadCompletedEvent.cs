// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Ignixa.Application.Events.Startup;

/// <summary>
/// Event raised when TenantPackagePreloadService completes loading all packages.
/// Subscribers can use this to perform additional startup tasks that depend on packages being loaded.
/// </summary>
public record TenantPackagePreloadCompletedEvent(
    int TenantCount,
    int PackagesLoaded,
    long ElapsedMilliseconds) : INotification;
