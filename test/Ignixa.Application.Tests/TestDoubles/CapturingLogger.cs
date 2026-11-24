// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Tests.TestDoubles;

/// <summary>Records every formatted message, and every exception attached to an entry, so a test can assert on what reached the log.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public List<Exception> Exceptions { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    bool ILogger.IsEnabled(LogLevel logLevel) => true;

    void ILogger.Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        if (exception is not null)
        {
            Exceptions.Add(exception);
        }
    }
}
