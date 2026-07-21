// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards for ADR 2607 (.NET 10 support with Core multi-targeting). Shippable Core libraries
/// must run on both supported runtimes, so any <c>src/Core/**</c> project that targets one of
/// {net9.0, net10.0} must target both. The netstandard2.0 analyzer projects do not intersect
/// that set and are therefore exempt without special-casing.
/// </summary>
public class RuntimeMultiTargetingGuardTests
{
    private static readonly string[] RequiredTargetFrameworks = ["net9.0", "net10.0"];

    [Fact]
    public void GivenCoreProjects_WhenTargetingASupportedRuntime_ThenTheyTargetBothRuntimes()
    {
        var violations = LoadCoreProjects()
            .Where(project => project.TargetFrameworks.Overlaps(RequiredTargetFrameworks))
            .Where(project => !RequiredTargetFrameworks.All(project.TargetFrameworks.Contains))
            .Select(project => $"{project.Name}: [{string.Join(';', project.TargetFrameworks)}]")
            .ToList();

        violations.ShouldBeEmpty(
            "Core libraries must multi-target both net9.0 and net10.0 (ADR 2607). " +
            "A project that targets one supported runtime must target both so it ships for each.");
    }

    private static IEnumerable<CoreProject> LoadCoreProjects()
    {
        var repoRoot = RepoRoot.Find();
        var coreDir = Path.Combine(repoRoot, "src", "Core");
        Directory.Exists(coreDir).ShouldBeTrue($"Expected Core source directory at {coreDir}.");

        var projects = Directory
            .EnumerateFiles(coreDir, "*.csproj", SearchOption.AllDirectories)
            .Select(ParseProject)
            .ToList();

        projects.ShouldNotBeEmpty("Expected to find Core projects; scan path may be wrong.");
        return projects;
    }

    private static CoreProject ParseProject(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        var properties = doc.Descendants("PropertyGroup").Elements().ToList();

        var single = properties.FirstOrDefault(element => element.Name.LocalName == "TargetFramework")?.Value;
        var multiple = properties.FirstOrDefault(element => element.Name.LocalName == "TargetFrameworks")?.Value;

        var frameworks = (multiple ?? single ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new CoreProject(Path.GetFileNameWithoutExtension(csprojPath), frameworks);
    }

    private sealed record CoreProject(string Name, HashSet<string> TargetFrameworks);
}
