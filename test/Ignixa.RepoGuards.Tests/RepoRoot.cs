// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Locates the repository root by walking up from a given start directory, defaulting to the test
/// output directory. Repo guards assert properties of the source tree rather than of compiled
/// behaviour, so they all need this.
/// </summary>
/// <remarks>
/// The walk accepts <c>.git</c> as either a directory or a file. In a git worktree or a submodule
/// it is a file holding a <c>gitdir:</c> pointer, so a directory-only check walks straight past the
/// worktree and returns the main checkout instead — silently making every guard assert against the
/// wrong source tree.
/// </remarks>
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

        dir.ShouldNotBeNull($"Could not find repo root: no .git file or directory in any ancestor of {startDirectory}");
        return dir!.FullName;
    }
}
