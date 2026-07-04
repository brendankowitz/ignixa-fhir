// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Thrown when a catalog-discovered factory method (a scenario or observation state) throws during
/// invocation. Wraps the original exception (available via <see cref="Exception.InnerException"/>)
/// rather than silently swallowing it.
/// </summary>
public sealed class ScenarioInvocationException(string message, Exception innerException)
    : Exception(message, innerException);
