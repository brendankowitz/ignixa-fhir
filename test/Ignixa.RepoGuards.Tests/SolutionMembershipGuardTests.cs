// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Asserts every test project is referenced by All.sln.
/// </summary>
/// <remarks>
/// CI builds and tests All.sln. A test project missing from it is never compiled and never run, and
/// nothing reports that: the suite stays green because the tests simply do not exist as far as the
/// build is concerned. Ignixa.DataLayer.SqlEntityFramework.Tests sat in exactly that state from the
/// repo's initial commit, silently accumulating ~360 compile errors while it held the only coverage
/// anywhere for _include, _revinclude, chained search and :iterate.
///
/// The failure is invisible by construction, which is why it needs a guard rather than a convention.
/// </remarks>
public class SolutionMembershipGuardTests
{
    [Fact]
    public void GivenAProjectUnderTest_WhenCheckingAllSln_ThenTheSolutionReferencesIt()
    {
        // Arrange
        var repoRoot = FindRepoRoot();
        var solutionPath = Path.Combine(repoRoot, "All.sln");
        File.Exists(solutionPath).ShouldBeTrue($"All.sln not found at {solutionPath}");

        var solutionText = File.ReadAllText(solutionPath);

        // A project is a test project when it pulls in the VSTest SDK -- that, not its folder or its
        // name, is what makes `dotnet test` discover and run it. test/ also holds tooling such as
        // Ignixa.Tests.Compatibility.CLI, which is an Exe and is correctly absent from the solution.
        var testProjects = Directory
            .GetFiles(Path.Combine(repoRoot, "test"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        testProjects.ShouldNotBeEmpty("Expected to find test projects under test/");

        // Act -- a project is referenced by file name; the solution stores a relative path ending in it.
        var missing = testProjects
            .Select(Path.GetFileName)
            .Where(fileName => !solutionText.Contains(fileName!, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        missing.ShouldBeEmpty(
            "These test projects are not in All.sln, so CI never builds or runs them and their tests " +
            "are silently absent rather than failing: " + string.Join(", ", missing) +
            ". Add them with `dotnet sln All.sln add <path>`, or delete them if genuinely obsolete.");
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
