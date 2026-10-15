// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir;

/// <summary>Console logging that never writes to stdout, which carries command or protocol output.</summary>
internal static class StandardErrorLogging
{
    public static ILoggerFactory Create(LogLevel minimumLevel) => LoggerFactory.Create(builder => builder
        .SetMinimumLevel(minimumLevel)
        .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace));
}
