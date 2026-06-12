// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

public class GitVersionCompositeActionGuardTests
{
    [Fact]
    public void GivenDotnetBuildAndTestAction_WhenReadingNuGetVersion_ThenItUsesMajorMinorPatch()
    {
        var actionYaml = LoadCompositeActionYaml();

        actionYaml.ShouldContain("value: ${{ steps.gitversion.outputs.majorMinorPatch }}");
        actionYaml.ShouldNotContain("value: ${{ steps.gitversion.outputs.nuGetVersion }}");
    }

    [Fact]
    public void GivenDotnetBuildAndTestAction_WhenBuildingSolution_ThenItUsesSemVerForVersionProperty()
    {
        var actionYaml = LoadCompositeActionYaml();

        actionYaml.ShouldContain("/p:Version=${{ steps.gitversion.outputs.semVer }}");
        actionYaml.ShouldNotContain("/p:Version=${{ steps.gitversion.outputs.nuGetVersion }}");
    }

    private static string LoadCompositeActionYaml()
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, ".github", "actions", "dotnet-build-and-test", "action.yml"));
    }

    private static string FindRepoRoot()
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
