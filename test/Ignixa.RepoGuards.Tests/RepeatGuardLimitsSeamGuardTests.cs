// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.FhirPath.Evaluation.Functions;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Keeps <see cref="RepeatGuardLimits.Scope"/> - the test-only seam that lowers <c>repeat()</c>'s
/// iteration and comparison guards - out of production code.
/// </summary>
/// <remarks>
/// <para>
/// The seam cannot enforce this itself: <c>Scope</c>'s constructor is <c>internal</c>, but
/// <c>InternalsVisibleTo</c> on <c>Ignixa.FhirPath</c> names the production assemblies
/// <c>Ignixa.SqlOnFhir</c> and <c>Ignixa.Search</c> as well as the test ones, and has no granularity
/// finer than the assembly. A failing test is the only enforcement available.
/// </para>
/// <para>
/// The rule is about <em>naming the type outside a doc comment</em>, not only about <c>new</c>: a
/// <c>&lt;see cref&gt;</c> is documentation and <c>CollectionFunctions</c> legitimately links to it, but a
/// field, local, alias or construction all mean production code is holding the seam. The patterns are
/// built from the type rather than written out, so a rename is a compile error in this file rather than a
/// regex that quietly stops matching while the scan reports green, and
/// <see cref="GivenASpellingThatNamesTheSeam_WhenScanned_ThenItIsDetected"/> exercises the matcher over
/// synthetic sources so it can be made to fail.
/// </para>
/// </remarks>
public class RepeatGuardLimitsSeamGuardTests
{
    private static readonly string SeamTypeName = typeof(RepeatGuardLimits).Name;

    private static readonly string ScopeTypeName = typeof(RepeatGuardLimits.Scope).Name;

    /// <summary>
    /// Where the seam is declared, excluded from the scan because a type may name itself. Derived from the
    /// type, so a rename that leaves the file behind fails
    /// <see cref="GivenTheSeamFileName_WhenResolved_ThenItExists"/> rather than silently excluding nothing.
    /// </summary>
    private static readonly string SeamFileName = $"{SeamTypeName}.cs";

    /// <summary>
    /// The three ways a file can reach the seam: naming the nested type, importing the outer type
    /// statically, or aliasing the outer type.
    /// </summary>
    /// <remarks>
    /// Applied to whole-file text rather than line by line: a fully-qualified reference broken across
    /// lines at the <c>.</c> is legal C# and defeats any per-line scan, and the alias and static-import
    /// forms admit <c>global::</c>, so the namespace character class has to include <c>:</c>.
    /// </remarks>
    private static readonly Regex[] SeamReferences =
    [
        new($@"\b{SeamTypeName}\s*\.\s*{ScopeTypeName}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new($@"using\s+static\s+[\w.:]*\b{SeamTypeName}\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new($@"using\s+\w+\s*=\s*[\w.:]*\b{SeamTypeName}\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant),
    ];

    [Fact]
    public void GivenProductionSources_WhenScanned_ThenNoneOutsideTheSeamNameItsScope()
    {
        var sources = ProductionSources();
        sources.Count.ShouldBeGreaterThan(500,
            "Found too few .cs files under src/ for this scan to be meaningful; the scan path is probably wrong.");

        var offenders = sources
            .Where(file => !string.Equals(Path.GetFileName(file), SeamFileName, StringComparison.Ordinal))
            .SelectMany(file => FindSeamReferences(File.ReadAllText(file))
                .Select(reference => $"{Relative(file)}({reference.Line}): {reference.Text}"))
            .ToList();

        offenders.ShouldBeEmpty(
            $"{SeamTypeName}.{ScopeTypeName} lowers repeat()'s iteration and comparison guards and exists only so "
            + "tests can prove those guards trip without paying their real-world scale. Production code "
            + "must never open one: a scope left around a production call path silently caps traversal and "
            + "turns a legitimate large repeat() into a 'possible infinite loop' failure. Ignixa.SqlOnFhir "
            + "and Ignixa.Search can see the type through InternalsVisibleTo, which is exactly why this "
            + "guard exists rather than a comment asking nicely." + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The one file the production scan excludes has to be the file the seam is declared in. If the type
    /// and its file diverge, the exclusion stops excluding and the seam's own declaration is reported as
    /// an offender - a real failure carrying a misleading reason.
    /// </summary>
    [Fact]
    public void GivenTheSeamFileName_WhenResolved_ThenItExists()
    {
        var declarations = ProductionSources()
            .Where(file => string.Equals(Path.GetFileName(file), SeamFileName, StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        declarations.ShouldHaveSingleItem(
            $"The production scan excludes '{SeamFileName}', derived from {SeamTypeName}'s own name. "
            + "Exactly one file under src/ must carry that name, or the exclusion no longer names the "
            + "seam's declaration.");
    }

    /// <summary>
    /// The positive control: without it the scan can go inert through a defect in the matcher itself and
    /// report green over a source tree it never examined. The alias and split-line cases are here because
    /// both defeated an earlier per-line, alias-blind revision while it reported no offenders.
    /// </summary>
    [Theory]
    [InlineData("using var scope = new RepeatGuardLimits.Scope(maxIterations: 5);")]
    [InlineData("using var scope = new Ignixa.FhirPath.Evaluation.Functions.RepeatGuardLimits.Scope();")]
    [InlineData("using var scope = new Ignixa.FhirPath.Evaluation.Functions.RepeatGuardLimits\n    .Scope();")]
    [InlineData("private RepeatGuardLimits.Scope? _held;")]
    [InlineData("using RGL = Ignixa.FhirPath.Evaluation.Functions.RepeatGuardLimits;")]
    [InlineData("using RGL = global::Ignixa.FhirPath.Evaluation.Functions.RepeatGuardLimits;")]
    [InlineData("using static Ignixa.FhirPath.Evaluation.Functions.RepeatGuardLimits;")]
    [InlineData("using static global::Ignixa.FhirPath.Evaluation.Functions.RepeatGuardLimits;")]
    public void GivenASpellingThatNamesTheSeam_WhenScanned_ThenItIsDetected(string source)
    {
        FindSeamReferences(source).ShouldNotBeEmpty(
            $"This spelling reaches {SeamTypeName}.{ScopeTypeName} and the production scan does not see it, "
            + "so the scan is inert for any file written this way.");
    }

    /// <summary>
    /// The negative control: documentation is not a mutator, and the scan must not turn a
    /// <c>&lt;see cref&gt;</c> into a build failure that can only be silenced by removing the link.
    /// </summary>
    [Theory]
    [InlineData("/// Substituted for the duration of one call via <see cref=\"RepeatGuardLimits.Scope\"/>.")]
    [InlineData("    /// <see cref=\"RepeatGuardLimits.Scope\"/> is test-only.")]
    [InlineData("using var scope = new SomethingElse.Scope();")]
    [InlineData("using Ignixa.FhirPath.Evaluation.Functions;")]
    public void GivenASpellingThatDoesNotHoldTheSeam_WhenScanned_ThenItIsNotReported(string source)
    {
        FindSeamReferences(source).ShouldBeEmpty(
            "The scan reports a line that does not hold the seam, so the only way to satisfy it is to "
            + "delete documentation or rename an unrelated type.");
    }

    /// <summary>
    /// Every line of <paramref name="source"/> that names the seam in code, with its 1-based line number.
    /// </summary>
    /// <remarks>
    /// Doc-comment lines are blanked rather than removed so line numbers still address the original file,
    /// and matching runs over the whole text so a reference split across lines cannot slip between two
    /// per-line matches.
    /// </remarks>
    private static IReadOnlyList<(int Line, string Text)> FindSeamReferences(string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var scannable = string.Join(
            '\n',
            lines.Select(line => line.TrimStart().StartsWith("///", StringComparison.Ordinal) ? string.Empty : line));

        return SeamReferences
            .SelectMany(pattern => pattern.Matches(scannable).Cast<Match>())
            .Select(match => LineAt(scannable, lines, match.Index))
            .DistinctBy(reference => reference.Line)
            .OrderBy(reference => reference.Line)
            .ToList();
    }

    private static (int Line, string Text) LineAt(string scannable, string[] lines, int index)
    {
        int line = scannable.Take(index).Count(character => character == '\n');
        return (line + 1, lines[line].Trim());
    }

    private static List<string> ProductionSources() =>
        [.. Directory
            .EnumerateFiles(Path.Combine(RepoRoot.Find(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    private static string Relative(string file) =>
        Path.GetRelativePath(RepoRoot.Find(), file).Replace(Path.DirectorySeparatorChar, '/');
}
