// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

public class RepoRootTests
{
    [Fact]
    public void GivenNestedPathUnderGitFile_WhenFindingRepoRoot_ThenReturnsMarkedRoot()
    {
        var root = CreateFixtureRoot();
        try
        {
            var nestedPath = Directory.CreateDirectory(Path.Combine(root, "nested", "path")).FullName;
            File.WriteAllText(Path.Combine(root, ".git"), "gitdir: unused");

            RepoRoot.Find(nestedPath).ShouldBe(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GivenNestedPathUnderGitDirectory_WhenFindingRepoRoot_ThenReturnsMarkedRoot()
    {
        var root = CreateFixtureRoot();
        try
        {
            var nestedPath = Directory.CreateDirectory(Path.Combine(root, "nested", "path")).FullName;
            Directory.CreateDirectory(Path.Combine(root, ".git"));

            RepoRoot.Find(nestedPath).ShouldBe(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateFixtureRoot() =>
        Directory.CreateDirectory(
            Path.Combine(AppContext.BaseDirectory, $"repo-root-tests-{Guid.NewGuid():N}")).FullName;
}
