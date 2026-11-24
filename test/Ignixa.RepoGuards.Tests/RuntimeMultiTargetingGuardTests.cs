// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards the ADR 2611 target framework table for shipped libraries and public tools. Each guarded project
/// must target exactly the frameworks of its row, so an extra, missing or replaced framework fails.
/// </summary>
public class RuntimeMultiTargetingGuardTests
{
    private static readonly string[] SupportedRuntimes = ["net10.0", "net11.0"];

    private static readonly string[] NetStandard = ["netstandard2.0"];

    // Analyzers, source generators and the content-only suites package.
    private static readonly HashSet<string> NetStandardProjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ignixa.Analyzers",
        "Ignixa.FhirPath.Generators",
        "Ignixa.Search.Sql.Generators",
        "Ignixa.TestScript.Suites",
    };

    private static readonly string[][] MultiTargetedProjectsOutsideCore =
    [
        ["src", "Application", "Ignixa.Sidecar.Contracts", "Ignixa.Sidecar.Contracts.csproj"],
        ["tools", "Ignixa.ConformanceMatrix.Cli", "Ignixa.ConformanceMatrix.Cli.csproj"],
        ["tools", "Ignixa.DeId.Cli", "Ignixa.DeId.Cli.csproj"],
        ["tools", "Ignixa.FhirFakes.Cli", "Ignixa.FhirFakes.Cli.csproj"],
        ["tools", "Ignixa.SqlOnFhir.Cli", "Ignixa.SqlOnFhir.Cli.csproj"],
        ["tools", "Ignixa.Validation.Cli", "Ignixa.Validation.Cli.csproj"],
    ];

    [Fact]
    public void GivenShippedLibrariesAndPublicTools_WhenTargetFrameworksAreRead_ThenEachMatchesItsAdrRow()
    {
        var violations = LoadGuardedProjects()
            .Select(project => (Project: project, Expected: ExpectedFrameworks(project.Name)))
            .Where(entry => !entry.Project.TargetFrameworks.SetEquals(entry.Expected))
            .Select(entry => $"{entry.Project.Name}: expected [{Format(entry.Expected)}], found [{Format(entry.Project.TargetFrameworks)}]")
            .ToList();

        violations.ShouldBeEmpty(
            "Core libraries, Ignixa.Sidecar.Contracts and the public CLIs target exactly net10.0;net11.0, " +
            "and the analyzer, generator and content-only projects target exactly netstandard2.0 (ADR 2611).");
    }

    private static string[] ExpectedFrameworks(string projectName) =>
        NetStandardProjects.Contains(projectName) ? NetStandard : SupportedRuntimes;

    private static string Format(IEnumerable<string> frameworks) =>
        string.Join(';', frameworks.Order(StringComparer.OrdinalIgnoreCase));

    private static List<GuardedProject> LoadGuardedProjects()
    {
        var repoRoot = RepoRoot.Find();
        var coreDir = Path.Combine(repoRoot, "src", "Core");
        Directory.Exists(coreDir).ShouldBeTrue($"Expected Core source directory at {coreDir}.");

        var corePaths = Directory.EnumerateFiles(coreDir, "*.csproj", SearchOption.AllDirectories).ToList();
        corePaths.ShouldNotBeEmpty("Expected to find Core projects; scan path may be wrong.");

        var outsideCorePaths = MultiTargetedProjectsOutsideCore
            .Select(segments => Path.Combine([repoRoot, .. segments]))
            .ToList();
        outsideCorePaths.Where(path => !File.Exists(path)).ShouldBeEmpty("A guarded project moved; update the ADR 2611 guard.");

        return corePaths.Concat(outsideCorePaths).Select(ParseProject).ToList();
    }

    private static GuardedProject ParseProject(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        var properties = doc.Descendants("PropertyGroup").Elements().ToList();

        var single = properties.FirstOrDefault(element => element.Name.LocalName == "TargetFramework")?.Value;
        var multiple = properties.FirstOrDefault(element => element.Name.LocalName == "TargetFrameworks")?.Value;

        var frameworks = (multiple ?? single ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new GuardedProject(Path.GetFileNameWithoutExtension(csprojPath), frameworks);
    }

    private sealed record GuardedProject(string Name, HashSet<string> TargetFrameworks);
}
