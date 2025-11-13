// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Provides access to the current FHIR request context.
/// Scoped per request (HTTP or background task).
/// Thread-safe for concurrent bundle processing via AsyncLocal storage.
/// </summary>
public interface IFhirRequestContextAccessor
{
    /// <summary>
    /// Gets or sets the current FHIR request context.
    /// Returns null if no context is available (e.g., during startup, health checks).
    /// For bundle processing, each concurrent entry has an isolated context.
    /// </summary>
    IFhirRequestContext? RequestContext { get; set; }
}
