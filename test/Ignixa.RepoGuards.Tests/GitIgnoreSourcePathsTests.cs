// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Repository hygiene guards. Currently asserts that source paths under directories
/// named "Packages" or "packages" (which the broad <c>**/[Pp]ackages/*</c> .gitignore
/// rule excludes) are NOT excluded, because each such path is real source code that
/// must ship with the repo.
/// </summary>
public class GitIgnoreSourcePathsTests
{
    public static IEnumerable<object[]> MustNotBeIgnored() => new[]
    {
        new object[] { "src/Application/Ignixa.Application/Features/Packages/PackageResourceImporter.cs" },
        new object[] { "src/Application/Ignixa.Application/Features/Packages/ImplementationGuideProvider.cs" },
        new object[] { "src/Core/Ignixa.SqlOnFhir/packages/SqlOnFhirEmbeddedPackage.cs" },
        new object[] { "src/Core/Ignixa.SqlOnFhir/packages/sql-on-fhir-v2/package/package.json" },
        new object[] { "test/Ignixa.Validation.Tests/TestHelpers/Packages/TestFhirPackage.cs" },
        new object[] { "test/Ignixa.Validation.Tests/TestHelpers/Packages/TestFhirPackageLoader.cs" },
        new object[] { "test/Ignixa.Validation.Tests/TestHelpers/Packages/CarinBbValidatorFactory.cs" },
        new object[] { "test/Ignixa.Application.Tests/Features/Packages/ImplementationGuideProviderTransitiveTests.cs" },
        new object[] { "test/Ignixa.Application.Tests/Features/Packages/ImplementationGuideProviderTransitiveIntegrationTests.cs" },
    };

    [Theory]
    [MemberData(nameof(MustNotBeIgnored))]
    public void GivenSourcePathUnderPackagesDir_WhenAskingGit_ThenPathIsNotIgnored(string relativePath)
    {
        var repoRoot = FindRepoRoot();
        var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(fullPath).ShouldBeTrue(
            $"Fixture path missing: {fullPath}. Update the guard data or restore the file.");

        var psi = new ProcessStartInfo("git", $"check-ignore --no-index \"{relativePath}\"")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        // git check-ignore --no-index exit code: 0 = path IS ignored by the rules
        // (regardless of whether it happens to be tracked), 1 = NOT ignored.
        // We need --no-index because the default 'check-ignore' short-circuits to
        // 'not ignored' for any path already in the index, which would hide the bug
        // we're guarding against.
        p.ExitCode.ShouldBe(
            1,
            $"Path '{relativePath}' is ignored by .gitignore (match: {stdout.Trim()}). " +
            "Update the Packages exception block in .gitignore.");
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
