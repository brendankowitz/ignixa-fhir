// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Keeps the Delta engine dependency opt-in. DeltaLake.Net ships native binaries per runtime identifier (well over
/// 100 MB each), so it may only be referenced by the experimental <c>Ignixa.SqlOnFhir.Fabric.Delta</c> library, and
/// no shippable project (Web, CLI, Application, API, the Parquet writers, tools) may reference that library. Test
/// projects may, because nothing references a test project, so they cannot carry the dependency into a product.
/// </summary>
public class FabricDeltaDependencyIsolationGuardTests
{
    private const string DeltaPackage = "DeltaLake.Net";
    private const string DeltaProject = "Ignixa.SqlOnFhir.Fabric.Delta";

    private static readonly string[] ScannedDirectories = ["src", "tools", "test", "bench", "codegen"];

    [Fact]
    public void GivenRepositoryBuildFiles_WhenReferencingTheDeltaEnginePackage_ThenOnlyTheFabricDeltaProjectDoes()
    {
        var referencing = LoadBuildFiles()
            .Where(file => file.Document.Descendants().Any(element =>
                element.Name.LocalName == "PackageReference" &&
                string.Equals((string?)element.Attribute("Include"), DeltaPackage, StringComparison.OrdinalIgnoreCase)))
            .Select(file => file.RelativePath)
            .ToList();

        referencing.ShouldBe(
            [$"src/Core/{DeltaProject}/{DeltaProject}.csproj"],
            $"Only {DeltaProject} may reference {DeltaPackage}; its native binaries must stay out of every other package and host.");
    }

    [Fact]
    public void GivenShippableProjects_WhenReferencingTheFabricDeltaLibrary_ThenNoneDoesByProjectOrPackage()
    {
        var violations = LoadBuildFiles()
            .Where(file => !file.RelativePath.StartsWith("test/", StringComparison.Ordinal))
            .Where(file => file.Document.Descendants().Any(ReferencesFabricDeltaLibrary))
            .Select(file => file.RelativePath)
            .ToList();

        violations.ShouldBeEmpty(
            $"{DeltaProject} is experimental and carries {DeltaPackage}'s native binaries; " +
            "shippable projects must not reference it as a project or a package (only test projects may).");
    }

    private static bool ReferencesFabricDeltaLibrary(XElement element)
    {
        var include = (string?)element.Attribute("Include");
        return element.Name.LocalName switch
        {
            "ProjectReference" => string.Equals(ProjectName(include), DeltaProject, StringComparison.OrdinalIgnoreCase),
            "PackageReference" => string.Equals(include, DeltaProject, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static List<BuildFile> LoadBuildFiles()
    {
        var repoRoot = RepoRoot.Find();
        var files = ScannedDirectories
            .Select(directory => Path.Combine(repoRoot, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(repoRoot, "Directory.Build.*", SearchOption.TopDirectoryOnly))
            .Where(IsBuildFile)
            .Select(path => new BuildFile(
                Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                XDocument.Load(path)))
            .ToList();

        files.ShouldContain(
            file => file.RelativePath.EndsWith($"/{DeltaProject}.csproj", StringComparison.Ordinal),
            "Expected to find the Fabric Delta project; the scan path may be wrong.");
        return files;
    }

    private static bool IsBuildFile(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(segment => segment is "bin" or "obj" or "node_modules"))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
    }

    // ProjectReference paths use Windows separators; normalize so the guard reads them the same on Linux CI.
    private static string? ProjectName(string? include) =>
        include is null ? null : Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));

    private sealed record BuildFile(string RelativePath, XDocument Document);
}
