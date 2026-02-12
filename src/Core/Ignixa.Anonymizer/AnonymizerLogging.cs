// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Ignixa.Anonymizer
{
    /// <summary>
    /// Provides a shared logger factory for anonymizer components that cannot use constructor-injected logging.
    /// </summary>
    internal static class AnonymizerLogging
    {
        public static ILoggerFactory LoggerFactory { get; set; } = new LoggerFactory();
        public static ILogger CreateLogger<T>() => LoggerFactory.CreateLogger<T>();
    }
}
