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
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull($"Could not find repo root from {AppContext.BaseDirectory}");
        return dir!.FullName;
    }
}
