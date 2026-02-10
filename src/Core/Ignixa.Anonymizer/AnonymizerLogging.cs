// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Ignixa.Anonymizer
{
    public static class AnonymizerLogging
    {
        public static ILoggerFactory LoggerFactory { get; set; } = new LoggerFactory();
        public static ILogger CreateLogger<T>() => LoggerFactory.CreateLogger<T>();
    }
}
