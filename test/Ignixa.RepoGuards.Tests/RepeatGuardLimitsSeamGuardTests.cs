// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Keeps <c>RepeatGuardLimits.Scope</c> - the test-only seam that lowers <c>repeat()</c>'s iteration
/// and comparison guards - out of production code.
/// </summary>
/// <remarks>
/// <para>
/// The seam cannot enforce this itself. <c>Scope</c>'s constructor is <c>internal</c>, and
/// <c>Ignixa.FhirPath.csproj</c> grants <c>InternalsVisibleTo</c> to <c>Ignixa.SqlOnFhir</c> and
/// <c>Ignixa.Search</c> as well as <c>Ignixa.FhirPath.Tests</c>, so two <em>production</em> assemblies
/// can open a scope today. <c>InternalsVisibleTo</c> has no finer granularity than the assembly, so the
/// only enforcement available is a build that fails, which is what this is.
/// </para>
/// <para>
/// The rule is deliberately about <em>naming the type outside a doc comment</em>, not only about
/// <c>new</c>. A <c>&lt;see cref&gt;</c> pointing at the seam is documentation and is not a mutator -
/// <c>CollectionFunctions</c> legitimately links to it - but a field, a local, a type alias or a
/// construction all name it in code, and any of those means production code is holding the seam.
/// <c>using static</c> is caught separately because it is the one spelling that would let a file
/// construct a <c>Scope</c> without naming <c>RepeatGuardLimits</c> at all.
/// </para>
/// </remarks>
public class RepeatGuardLimitsSeamGuardTests
{
    private const string SeamFileName = "RepeatGuardLimits.cs";

    private static readonly Regex NamesTheScope = new(
        @"RepeatGuardLimits\s*\.\s*Scope",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ImportsTheSeamStatically = new(
        @"using\s+static\s+[\w.]*\bRepeatGuardLimits\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void GivenProductionSources_WhenScanned_ThenNoneOutsideTheSeamNameItsScope()
    {
        var sources = ProductionSources();
        sources.Count.ShouldBeGreaterThan(500,
            "Found too few .cs files under src/ for this scan to be meaningful; the scan path is probably wrong.");

        var offenders = sources
            .Where(file => !string.Equals(Path.GetFileName(file), SeamFileName, StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file)
                .Select((text, index) => (text, number: index + 1))
                .Where(line => !line.text.TrimStart().StartsWith("///", StringComparison.Ordinal))
                .Where(line => NamesTheScope.IsMatch(line.text) || ImportsTheSeamStatically.IsMatch(line.text))
                .Select(line => $"{Relative(file)}({line.number}): {line.text.Trim()}"))
            .ToList();

        offenders.ShouldBeEmpty(
            "RepeatGuardLimits.Scope lowers repeat()'s iteration and comparison guards and exists only so "
            + "tests can prove those guards trip without paying their real-world scale. Production code "
            + "must never open one: a scope left around a production call path silently caps traversal and "
            + "turns a legitimate large repeat() into a 'possible infinite loop' failure. Ignixa.SqlOnFhir "
            + "and Ignixa.Search can see the type through InternalsVisibleTo, which is exactly why this "
            + "guard exists rather than a comment asking nicely." + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Without this the guard passes green on a regex that no longer matches how the seam is spelled -
    /// the same can't-fail mode it exists to prevent.
    /// </summary>
    [Fact]
    public void GivenTestSources_WhenScanned_ThenTheSeamIsStillSpelledTheWayThisGuardMatches()
    {
        var users = Directory
            .EnumerateFiles(Path.Combine(RepoRoot.Find(), "test"), "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadLines(file).Any(line => NamesTheScope.IsMatch(line)))
            .Select(Relative)
            .ToList();

        users.ShouldNotBeEmpty(
            "No file under test/ names RepeatGuardLimits.Scope. Either the seam was removed - in which "
            + "case delete this guard - or it is spelled some way this guard no longer recognises, and the "
            + "production scan above is now inert.");
    }

    private static List<string> ProductionSources() =>
        [.. Directory
            .EnumerateFiles(Path.Combine(RepoRoot.Find(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    private static string Relative(string file) =>
        Path.GetRelativePath(RepoRoot.Find(), file).Replace(Path.DirectorySeparatorChar, '/');
}
