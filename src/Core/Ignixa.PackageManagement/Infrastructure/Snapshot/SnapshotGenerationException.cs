// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.PackageManagement.Infrastructure.Snapshot;

/// <summary>
/// Thrown when snapshot generation cannot proceed for a structural reason — most notably a
/// circular <c>baseDefinition</c> chain. This is an explicit, fail-fast signal for malformed
/// conformance input; callers at the composition boundary (see
/// <see cref="ProfileLayeredSchemaProvider"/>) catch it, log it, and downgrade the profile to
/// base-only validation rather than propagating a broken snapshot downstream.
/// </summary>
public sealed class SnapshotGenerationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SnapshotGenerationException"/> class.</summary>
    public SnapshotGenerationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SnapshotGenerationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public SnapshotGenerationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SnapshotGenerationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SnapshotGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
