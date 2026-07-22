// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Locates the repository root from the test output directory. Repo guards assert properties of
/// the source tree rather than of compiled behaviour, so they all need this.
/// </summary>
internal static class RepoRoot
{
    public static string Find() => Find(AppContext.BaseDirectory);

    public static string Find(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
               !File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull($"Could not find repo root from {startDirectory}");
        return dir!.FullName;
    }
}
