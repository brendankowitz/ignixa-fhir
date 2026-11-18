// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Tests.TestDoubles;

/// <summary>
/// Records everything a log provider would write for each entry: the formatted message and, as a provider prints it,
/// the exception passed alongside it.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    bool ILogger.IsEnabled(LogLevel logLevel) => true;

    void ILogger.Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(formatter(state, exception) + Environment.NewLine + exception);
}
