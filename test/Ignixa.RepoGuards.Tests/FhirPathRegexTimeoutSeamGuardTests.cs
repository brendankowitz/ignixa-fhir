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
/// Keeps <see cref="FhirPathRegex.TimeoutScope"/> - the test-only seam that shortens the regex match timeout -
/// out of production code.
/// </summary>
/// <remarks>
/// Same reasoning as <see cref="RepeatGuardLimitsSeamGuardTests"/>: the seam's constructor is internal, but
/// <c>InternalsVisibleTo</c> also names the production assemblies <c>Ignixa.SqlOnFhir</c> and
/// <c>Ignixa.Search</c>. A scope held in production would change how long a pattern may run before it fails.
/// Names are taken from the types so a rename breaks this file rather than silently matching nothing.
/// </remarks>
public class FhirPathRegexTimeoutSeamGuardTests
{
    private static readonly string OwnerTypeName = typeof(FhirPathRegex).Name;

    private static readonly string ScopeTypeName = typeof(FhirPathRegex.TimeoutScope).Name;

    private static readonly string OwnerFileName = $"{OwnerTypeName}.cs";

    private static readonly Regex[] SeamReferences =
    [
        new($@"\b{OwnerTypeName}\s*\.\s*{ScopeTypeName}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new($@"using\s+static\s+[\w.:]*\b{OwnerTypeName}\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new($@"using\s+\w+\s*=\s*[\w.:]*\b{OwnerTypeName}\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant),
    ];

    [Fact]
    public void GivenProductionSources_WhenScanned_ThenNoneOutsideTheOwnerOpensTheTimeoutScope()
    {
        var sources = ProductionSources();
        sources.Count.ShouldBeGreaterThan(500,
            "Found too few .cs files under src/ for this scan to be meaningful; the scan path is probably wrong.");
        sources.Count(file => string.Equals(Path.GetFileName(file), OwnerFileName, StringComparison.Ordinal))
            .ShouldBe(1, $"The scan excludes '{OwnerFileName}'; exactly one file under src/ must carry that name.");

        var offenders = sources
            .Where(file => !string.Equals(Path.GetFileName(file), OwnerFileName, StringComparison.Ordinal))
            .Where(file => HoldsSeam(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepoRoot.Find(), file))
            .ToList();

        offenders.ShouldBeEmpty(
            $"{OwnerTypeName}.{ScopeTypeName} shortens the regex match timeout for tests. Production code must not "
            + "open one." + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Theory]
    [InlineData("using var scope = new FhirPathRegex.TimeoutScope(TimeSpan.FromMilliseconds(1));")]
    [InlineData("using var scope = new Ignixa.FhirPath.Evaluation.Functions.FhirPathRegex\n    .TimeoutScope(t);")]
    [InlineData("using R = Ignixa.FhirPath.Evaluation.Functions.FhirPathRegex;")]
    [InlineData("using static Ignixa.FhirPath.Evaluation.Functions.FhirPathRegex;")]
    public void GivenASpellingThatNamesTheSeam_WhenScanned_ThenItIsDetected(string source)
    {
        HoldsSeam(source).ShouldBeTrue("The production scan does not see this spelling of the seam.");
    }

    [Theory]
    [InlineData("/// See <see cref=\"FhirPathRegex.TimeoutScope\"/>.")]
    [InlineData("var regex = FhirPathRegex.Get(pattern, options, \"matches\");")]
    public void GivenASpellingThatDoesNotHoldTheSeam_WhenScanned_ThenItIsNotReported(string source)
    {
        HoldsSeam(source).ShouldBeFalse("The scan reports a line that does not hold the seam.");
    }

    private static bool HoldsSeam(string source)
    {
        var code = string.Join(
            '\n',
            source.ReplaceLineEndings("\n").Split('\n')
                .Select(line => line.TrimStart().StartsWith("///", StringComparison.Ordinal) ? string.Empty : line));
        return SeamReferences.Any(pattern => pattern.IsMatch(code));
    }

    private static List<string> ProductionSources() =>
        [.. Directory
            .EnumerateFiles(Path.Combine(RepoRoot.Find(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
}
