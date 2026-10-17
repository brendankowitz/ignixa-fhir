// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>Whether a failed remote request may succeed if asked again, and how it failed.</summary>
public enum TransientFailure
{
    /// <summary>A definitive failure: asking again gets the same answer (a 4xx, a malformed answer, an OperationOutcome).</summary>
    None,

    /// <summary>The server did not answer within the timeout; asking again would wait as long.</summary>
    Timeout,

    /// <summary>The server could not be reached or answered 5xx; asking again may succeed.</summary>
    Unavailable,
}
