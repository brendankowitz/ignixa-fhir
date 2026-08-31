// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Locator;
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

    private static readonly ConcurrentDictionary<string, string> EffectiveStabilities =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly VisualStudioInstance MsBuildInstance = MSBuildLocator.RegisterDefaults();

    [Fact]
    public void GivenPackableProjects_WhenComparingToDependencies_ThenNoPackageIsMoreStableThanItsDependencies()
    {
        var projects = LoadPackableProjects();

        var violations = projects.Values
            .Where(project => project.IsPublicFeed)
            .SelectMany(project => FindStabilityViolations(project, projects))
            .ToList();

        violations.ShouldBeEmpty(
            "A public-feed package must not be more stable than any package it depends on (ADR 2606). " +
            "Either lower the package's PackageStability or graduate its dependencies first. " +
            "Internal Application/DataLayer packages publish stable and are exempt (ADR 2607).");
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

    [Fact]
    public void GivenNestedPropsWithoutImport_WhenResolvingStability_ThenUnreachableAncestorPolicyIsNotUsed()
    {
        var repoRoot = Directory.CreateTempSubdirectory();

        try
        {
            var applicationDirectory = Directory.CreateDirectory(Path.Combine(repoRoot.FullName, "src", "Application"));
            File.WriteAllText(
                Path.Combine(applicationDirectory.FullName, "Directory.Build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <PackageStability Condition="'$(PackageStability)' == ''">stable</PackageStability>
                  </PropertyGroup>
                </Project>
                """);

            var projectDirectory = Directory.CreateDirectory(Path.Combine(applicationDirectory.FullName, "Nested"));
            File.WriteAllText(
                Path.Combine(projectDirectory.FullName, "Directory.Build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <PackageStability>beta</PackageStability>
                  </PropertyGroup>
                </Project>
                """);

            var projectPath = Path.Combine(projectDirectory.FullName, "TestProject.csproj");
            File.WriteAllText(projectPath, """<Project Sdk="Microsoft.NET.Sdk" />""");

            GetEffectiveStability(projectPath).ShouldBe("beta");
        }
        finally
        {
            repoRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void GivenProjectEvaluationFailure_WhenResolvingStability_ThenGuardFailsWithDiagnostics()
    {
        var projectDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var projectPath = Path.Combine(projectDirectory.FullName, "Malformed.csproj");
            File.WriteAllText(projectPath, "<Project");

            var exception = Should.Throw<InvalidOperationException>(() => GetEffectiveStability(projectPath));

            exception.Message.ShouldContain(projectPath);
        }
        finally
        {
            projectDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void GivenMultiTargetProjectWithDifferentStability_WhenResolvingStability_ThenGuardFails()
    {
        var projectDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var projectPath = Path.Combine(projectDirectory.FullName, "MultiTarget.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
                    <PackageStability>alpha</PackageStability>
                    <PackageStability Condition="'$(TargetFramework)' == 'net9.0'">beta</PackageStability>
                    <PackageStability Condition="'$(TargetFramework)' == 'net10.0'">stable</PackageStability>
                  </PropertyGroup>
                </Project>
                """);

            var exception = Should.Throw<InvalidOperationException>(() => GetEffectiveStability(projectPath));

            exception.Message.ShouldContain("PackageStability differs by target framework");
            exception.Message.ShouldContain(projectPath);
            exception.Message.ShouldContain("default is 'alpha'");
            exception.Message.ShouldContain("'net9.0' is 'beta'");
        }
        finally
        {
            projectDirectory.Delete(recursive: true);
        }
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
        var repoRoot = RepoRoot.Find();
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
        var projectIsPackable = !string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase);

        return new PackableProject(
            Name: Path.GetFileNameWithoutExtension(csprojPath),
            FullPath: fullPath,
            RelativePath: relativePath,
            IsPackable: projectIsPackable,
            DeclaredStability: declaredStability,
            EffectiveStability: projectIsPackable ? GetEffectiveStability(fullPath) : DefaultStability,
            ProjectReferences: references);
    }

    private static string GetEffectiveStability(string csprojPath)
    {
        return EffectiveStabilities.GetOrAdd(Path.GetFullPath(csprojPath), EvaluatePackageStability);
    }

    private static string EvaluatePackageStability(string csprojPath)
    {
        try
        {
            var stability = EvaluatePackageStability(csprojPath, targetFramework: null);
            foreach (var targetFramework in EvaluateTargetFrameworks(csprojPath))
            {
                var targetStability = EvaluatePackageStability(csprojPath, targetFramework);
                if (!string.Equals(stability, targetStability, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"PackageStability differs by target framework for '{csprojPath}': " +
                        $"default is '{stability}', but '{targetFramework}' is '{targetStability}'.");
                }
            }

            return stability;
        }
        catch (InvalidProjectFileException exception)
        {
            throw new InvalidOperationException(
                $"Failed to evaluate PackageStability for '{csprojPath}': {exception.Message}",
                exception);
        }
    }

    private static IEnumerable<string> EvaluateTargetFrameworks(string csprojPath)
    {
        var targetFrameworks = EvaluateProjectProperty(csprojPath, targetFramework: null, "TargetFrameworks");
        if (!string.IsNullOrWhiteSpace(targetFrameworks))
        {
            return targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var targetFramework = EvaluateProjectProperty(csprojPath, targetFramework: null, "TargetFramework");
        return string.IsNullOrWhiteSpace(targetFramework) ? [] : [targetFramework];
    }

    private static string EvaluatePackageStability(string csprojPath, string? targetFramework)
    {
        var stability = EvaluateProjectProperty(csprojPath, targetFramework, "PackageStability");
        if (string.IsNullOrWhiteSpace(stability))
        {
            throw new InvalidOperationException($"PackageStability was not evaluated for '{csprojPath}'.");
        }

        return stability;
    }

    private static string EvaluateProjectProperty(string csprojPath, string? targetFramework, string propertyName)
    {
        _ = MsBuildInstance;

        var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BasePackageVersion"] = "0.0.0",
            ["Configuration"] = "Release",
        };
        if (targetFramework is not null)
        {
            globalProperties["TargetFramework"] = targetFramework;
        }

        using var projectCollection = new ProjectCollection(globalProperties);
        var project = projectCollection.LoadProject(csprojPath);
        return project.GetPropertyValue(propertyName);
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

    private sealed record PackableProject(
        string Name,
        string FullPath,
        string RelativePath,
        bool IsPackable,
        string? DeclaredStability,
        string EffectiveStability,
        List<string> ProjectReferences)
    {
        public string Stability => EffectiveStability;

        public bool IsPublicFeed =>
            RelativePath.StartsWith($"src{Path.DirectorySeparatorChar}Core{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            RelativePath.StartsWith($"tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            Name == "Ignixa.Sidecar.Contracts";
    }
}
