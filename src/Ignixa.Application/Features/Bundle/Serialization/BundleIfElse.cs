// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// Helper class for conditional serialization chains in FhirJsonWriter.
/// Enables if-elseif-else patterns for conditional JSON property writing.
/// </summary>
internal sealed class BundleIfElse
{
    private readonly FhirJsonWriter _writer;
    private readonly bool _hasRun;

    /// <summary>
    /// Initializes a new instance of the BundleIfElse class.
    /// </summary>
    /// <param name="writer">The FhirJsonWriter instance.</param>
    /// <param name="hasRun">Whether the initial condition was executed.</param>
    internal BundleIfElse(FhirJsonWriter writer, bool hasRun)
    {
        _writer = writer;
        _hasRun = hasRun;
    }

    /// <summary>
    /// Conditionally executes the provided action if no previous condition has run and this predicate is true.
    /// </summary>
    /// <param name="predicate">Condition to test.</param>
    /// <param name="action">Action to execute if condition is true and no previous condition has run.</param>
    /// <returns>The FhirJsonWriter instance for chaining.</returns>
    public FhirJsonWriter ElseIf(bool predicate, Action<FhirJsonWriter> action)
    {
        if (!_hasRun && predicate)
        {
            action(_writer);
        }

        return _writer;
    }
}
