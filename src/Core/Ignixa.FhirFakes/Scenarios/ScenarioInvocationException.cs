// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Thrown by <see cref="ScenarioCatalog.Invoke"/> when the underlying scenario factory method throws
/// during invocation. Wraps the original exception (available via <see cref="Exception.InnerException"/>)
/// rather than silently swallowing it.
/// </summary>
public sealed class ScenarioInvocationException : Exception
{
    public ScenarioInvocationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
