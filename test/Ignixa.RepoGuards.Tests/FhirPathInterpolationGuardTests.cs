// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Keeps values out of FHIRPath expression text in <c>src/</c> (#536).
/// </summary>
/// <remarks>
/// <para>
/// Interpolating a request value into an expression such as
/// <c>$"rest.resource.where(type = '{ResourceType}')..."</c> lets a value containing a quote change what the
/// expression means (<c>Patient' or true or type = '</c> made the capability check pass for any type), and puts
/// caller-chosen text into the process-wide expression cache. Pass the value as an environment variable
/// (<c>%name</c> via <c>EvaluationContext.WithEnvironmentVariable</c>) so the expression text is constant.
/// </para>
/// <para>
/// Two patterns are flagged, on whole-file text with comment lines blanked: an interpolated literal that
/// holds a quoted hole (<c>'{x}'</c>) next to a FHIRPath function call, and an interpolated literal passed
/// directly as the expression argument of <c>Select</c>, <c>Scalar</c>, <c>IsTrue</c>, <c>IsBoolean</c>,
/// <c>Predicate</c> or <c>Parse</c>. It is a heuristic: an expression assembled by concatenation or from a
/// variable that is later interpolated is not seen, which is what review is for.
/// </para>
/// </remarks>
public class FhirPathInterpolationGuardTests
{
    private const string SourcePath = "src";

    private static readonly Regex[] InterpolationPatterns =
    [
        // $"...where(type = '{value}')..." - a quoted hole beside a FHIRPath function call.
        new(
            @"\$@?""[^""\n]*\.(?:where|exists|first|select|all|ofType|startsWith|endsWith|contains|extension|matches|iif)\([^""\n]*'\{[^}""\n]+\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        // .Select($"...{value}...") - any interpolated expression argument.
        new(
            @"\.(?:Select|Scalar|IsTrue|IsBoolean|Predicate|Parse)\(\s*\$@?""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),
    ];

    /// <summary>
    /// Interpolations that are not FHIRPath, with the reason. Keyed by path relative to <see cref="SourcePath"/>;
    /// the value is how many the file may hold. An entry whose count no longer matches fails the scan, so the
    /// list cannot outlive the code it excuses.
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Allowlist = new(StringComparer.Ordinal)
    {
        ["Core/Ignixa.NarrativeGenerator/Security/XhtmlSanitizer.cs"] = (1,
            "XDocument.Parse over an XHTML narrative wrapper, not a FHIRPath expression. The pattern matches "
            + "any Parse( call; the input is sanitized as XML by this class."),
        ["Application/Ignixa.Application.BackgroundOperations/BulkDelete/Activities/BulkDeleteBatchActivity.cs"] = (1,
            "parameterParser.Parse over a search query string whose values are Uri.EscapeDataString-encoded; "
            + "it is a URL query, not a FHIRPath expression."),
    };

    [Fact]
    public void GivenSourceFiles_WhenScanned_ThenNoValueIsInterpolatedIntoFhirPathOutsideTheAllowlist()
    {
        var root = Path.Combine(RepoRoot.Find(), SourcePath);
        var sources = Sources(root);
        sources.Count.ShouldBeGreaterThan(500,
            $"Found too few .cs files under {SourcePath} for this scan to be meaningful; the scan path is probably wrong.");

        var found = sources
            .Select(file => (File: Relative(root, file), Hits: FindInterpolations(File.ReadAllText(file))))
            .Where(entry => entry.Hits.Count > 0)
            .ToList();

        var offenders = found
            .Where(entry => !Allowlist.TryGetValue(entry.File, out var allowed) || allowed.Count != entry.Hits.Count)
            .SelectMany(entry => entry.Hits.Select(hit => $"{SourcePath}/{entry.File}({hit.Line}): {hit.Text}"))
            .ToList();

        var staleEntries = Allowlist.Keys
            .Where(file => found.All(entry => !string.Equals(entry.File, file, StringComparison.Ordinal)))
            .ToList();

        offenders.ShouldBeEmpty(
            "A value interpolated into FHIRPath text can change the meaning of the expression and fills the "
            + "expression cache with caller-chosen keys. Pass it as an environment variable (%name) through "
            + "EvaluationContext.WithEnvironmentVariable instead. If the interpolation is not FHIRPath, add it "
            + "to the allowlist in this file with a reason."
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        staleEntries.ShouldBeEmpty("These allowlist entries no longer hold an interpolation; remove them.");
    }

    /// <summary>
    /// The positive control: without it a defect in the matcher would let the scan report green over files
    /// it never really examined.
    /// </summary>
    [Theory]
    [InlineData("""var e = $"rest.resource.where(type = '{ResourceType}').exists()";""")]
    [InlineData("""x.Select($"snapshot.element.where(path='{path}')")""")]
    [InlineData("""element.Scalar($"{path}.fixedString")""")]
    [InlineData("""element.IsTrue( $"{expression}" )""")]
    public void GivenAnInterpolatedFhirPathExpression_WhenScanned_ThenItIsDetected(string source)
    {
        FindInterpolations(source).ShouldNotBeEmpty("The scan does not see this interpolation, so it is inert for code written this way.");
    }

    /// <summary>
    /// The negative control: constant expressions, variable bindings and ordinary messages are not reported.
    /// </summary>
    [Theory]
    [InlineData("""var e = "rest.resource.where(type = %resourceType).exists()";""")]
    [InlineData("""x.Select("snapshot.element.where(path = %path)", context)""")]
    [InlineData("""throw new BadRequestException($"Resource type '{resourceType}' is not supported");""")]
    [InlineData("""var e = $"{baseName}.ofType({variant})";""")]
    [InlineData("""// var e = $"rest.resource.where(type = '{ResourceType}')";""")]
    public void GivenAnExpressionWithoutInterpolatedValues_WhenScanned_ThenItIsNotReported(string source)
    {
        FindInterpolations(source).ShouldBeEmpty("The scan reports text that does not interpolate a value into FHIRPath.");
    }

    private static IReadOnlyList<(int Line, string Text)> FindInterpolations(string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var scannable = string.Join(
            '\n',
            lines.Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? string.Empty : line));

        return InterpolationPatterns
            .SelectMany(pattern => pattern.Matches(scannable).Cast<Match>())
            .Select(match => LineAt(scannable, lines, match.Index))
            .DistinctBy(hit => hit.Line)
            .OrderBy(hit => hit.Line)
            .ToList();
    }

    private static (int Line, string Text) LineAt(string scannable, string[] lines, int index)
    {
        int line = scannable.Take(index).Count(character => character == '\n');
        return (line + 1, lines[line].Trim());
    }

    private static List<string> Sources(string root) =>
        [.. Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
}
