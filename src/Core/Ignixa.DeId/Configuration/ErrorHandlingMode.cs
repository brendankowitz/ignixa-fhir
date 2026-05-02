// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Serialization;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// Error handling strategies for de-identification processing.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorHandlingMode
{
    /// <summary>
    /// Stop processing and return error on first failure.
    /// </summary>
    StopOnError,

    /// <summary>
    /// Log errors and continue processing (skip failed nodes).
    /// </summary>
    LogAndContinue,

    /// <summary>
    /// Fail immediately on any error (no partial results).
    /// </summary>
    FailFast
}
