// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Runs a system command for a test (mount, ln, stat), reading what it writes so that it cannot block on a full pipe.</summary>
internal static class TestProcess
{
    /// <summary>Whether <paramref name="command"/> ran and exited with 0; false when it is not installed.</summary>
    public static bool TryRun(string command, params string[] arguments) => Run(command, arguments) is { ExitCode: 0 };

    /// <summary>The command's exit code and standard output, or null when it is not installed.</summary>
    public static (int ExitCode, string Output, string Errors)? Run(string command, params string[] arguments)
    {
        var info = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
