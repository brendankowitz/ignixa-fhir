// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards for ADR 2606 (NuGet package stability versioning). NuGet's NU5104 only catches
/// stable→pre-release dependencies at pack time; these tests additionally enforce the
/// beta→alpha case and require every public package to declare PackageStability explicitly
/// so new packages cannot silently ship under the default classification.
/// </summary>
public class PackageStabilityGuardTests
{
    private const string DefaultStability = "alpha";

    private static readonly Dictionary<string, int> StabilityRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = 0,
        ["beta"] = 1,
        ["stable"] = 2,
    };

    [Fact]
    public void GivenPackableProjects_WhenComparingToDependencies_ThenNoPackageIsMoreStableThanItsDependencies()
    {
        var projects = LoadPackableProjects();

        var violations = projects.Values
            .SelectMany(project => FindStabilityViolations(project, projects))
            .ToList();

        violations.ShouldBeEmpty(
            "A package must not be more stable than any package it depends on (ADR 2606). " +
            "Either lower the package's PackageStability or graduate its dependencies first.");
    }

    [Fact]
    public void GivenPublicPackages_WhenCheckingClassification_ThenPackageStabilityIsExplicit()
    {
        var unclassified = LoadPackableProjects().Values
            .Where(project => project.IsPublicFeed && project.DeclaredStability is null)
            .Select(project => project.Name)
            .ToList();

        unclassified.ShouldBeEmpty(
            "Public packages (src/Core, tools, Sidecar.Contracts) must declare <PackageStability> " +
            "explicitly per ADR 2606. Unclassified packages would silently publish as alpha.");
    }

    [Fact]
    public void GivenPackableProjects_WhenReadingStability_ThenValuesAreKnown()
    {
        var invalid = LoadPackableProjects().Values
            .Where(project => project.DeclaredStability is not null && !StabilityRank.ContainsKey(project.DeclaredStability))
            .Select(project => $"{project.Name}: '{project.DeclaredStability}'")
            .ToList();

        invalid.ShouldBeEmpty("PackageStability must be one of: alpha, beta, stable.");
    }

    private static IEnumerable<string> FindStabilityViolations(
        PackableProject project,
        Dictionary<string, PackableProject> projects)
    {
        foreach (var referencePath in project.ProjectReferences)
        {
            if (projects.TryGetValue(referencePath, out var dependency) &&
                StabilityRank[project.Stability] > StabilityRank[dependency.Stability])
            {
                yield return $"{project.Name} ({project.Stability}) -> {dependency.Name} ({dependency.Stability})";
            }
        }
    }

    private static Dictionary<string, PackableProject> LoadPackableProjects()
    {
        var repoRoot = FindRepoRoot();
        string[] scanDirs = ["src", "tools"];

        var projects = scanDirs
            .Select(dir => Path.Combine(repoRoot, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories))
            .Select(csprojPath => ParseProject(csprojPath, repoRoot))
            .Where(project => project.IsPackable)
            .ToDictionary(project => project.FullPath, StringComparer.OrdinalIgnoreCase);

        projects.ShouldNotBeEmpty("Expected to find packable projects; scan paths may be wrong.");
        return projects;
    }

    private static PackableProject ParseProject(string csprojPath, string repoRoot)
    {
        var doc = XDocument.Load(csprojPath);
        var properties = doc.Descendants("PropertyGroup").Elements().ToList();

        var isPackable = properties
            .FirstOrDefault(element => element.Name.LocalName == "IsPackable")?.Value.Trim();
        var declaredStability = properties
            .FirstOrDefault(element => element.Name.LocalName == "PackageStability")?.Value.Trim();

        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var references = doc.Descendants("ProjectReference")
            .Where(IsNuspecDependency)
            .Select(reference => Path.GetFullPath(Path.Combine(projectDir, reference.Attribute("Include")!.Value)))
            .ToList();

        var fullPath = Path.GetFullPath(csprojPath);
        var relativePath = Path.GetRelativePath(repoRoot, fullPath);

        return new PackableProject(
            Name: Path.GetFileNameWithoutExtension(csprojPath),
            FullPath: fullPath,
            RelativePath: relativePath,
            IsPackable: !string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase),
            DeclaredStability: declaredStability,
            ProjectReferences: references);
    }

    // Analyzer/source-generator references (ReferenceOutputAssembly=false) and PrivateAssets="All"
    // references are not recorded as nuspec dependencies, so they don't constrain stability.
    private static bool IsNuspecDependency(XElement reference)
    {
        var referenceOutput = reference.Element("ReferenceOutputAssembly")?.Value
            ?? reference.Attribute("ReferenceOutputAssembly")?.Value;
        if (string.Equals(referenceOutput?.Trim(), "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var privateAssets = reference.Element("PrivateAssets")?.Value
            ?? reference.Attribute("PrivateAssets")?.Value;
        return !string.Equals(privateAssets?.Trim(), "all", StringComparison.OrdinalIgnoreCase);
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

    private sealed record PackableProject(
        string Name,
        string FullPath,
        string RelativePath,
        bool IsPackable,
        string? DeclaredStability,
        List<string> ProjectReferences)
    {
        public string Stability => DeclaredStability ?? DefaultStability;

        public bool IsPublicFeed =>
            RelativePath.StartsWith($"src{Path.DirectorySeparatorChar}Core{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            RelativePath.StartsWith($"tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            Name == "Ignixa.Sidecar.Contracts";
    }
}
