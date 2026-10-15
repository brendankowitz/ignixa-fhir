// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// No C# source or project file under <c>src/</c>, <c>tools/</c> or <c>test/</c> may match a .gitignore rule. An ignored
/// source file builds locally and is silently left out of every commit (the broad <c>**/[Pp]ackages/*</c> rule hid a whole
/// source folder this way); a force-added one is tracked but its new neighbours are not.
/// </summary>
public class IgnoredSourceFilesGuardTests
{
    private static readonly string[] Roots = ["src", "tools", "test"];

    // Regression: at 96d6150 the force-added tools/Ignixa.CliOnFhir/Terminology/Packages/*.cs matched **/[Pp]ackages/*.
    [SkippableFact]
    public void GivenTheWorkingTree_WhenGitListsIgnoredFiles_ThenNoSourceFileIsIgnored()
    {
        string root = RepoRoot.Find();

        List<string> untracked = IgnoredSources(root, "--others");
        List<string> tracked = IgnoredSources(root, "--cached");

        untracked.ShouldBeEmpty($"These source files are ignored by .gitignore and would be left out of commits: {string.Join(", ", untracked)}. Rename the folder or add an exception.");
        tracked.ShouldBeEmpty($"These tracked source files match a .gitignore rule, so files added beside them would be ignored: {string.Join(", ", tracked)}. Rename the folder or add an exception.");
    }

    private static List<string> IgnoredSources(string root, string which)
    {
        string output = Git(root, ["ls-files", which, "--ignored", "--exclude-standard", "--", .. Roots]);
        return [.. output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(path => !path.Split('/').Any(segment => segment is "bin" or "obj"))
            .Order(StringComparer.Ordinal)];
    }

    private static string Git(string root, string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Win32Exception ex)
        {
            Skip.If(true, $"git is not available to list ignored files ({ex.Message}).");
            throw;
        }

        using (process)
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Skip.If(process.ExitCode == 128 && error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase), $"{root} is not a git working tree, so ignored files cannot be listed.");
            process.ExitCode.ShouldBe(0, $"git {string.Join(' ', arguments)} failed: {error}");
            return output;
        }
    }
}
