// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Keeps catch-all exception handlers out of <c>src/Core/Ignixa.FhirPath</c> (#538).
/// </summary>
/// <remarks>
/// <para>
/// A bare <c>catch</c> or an unfiltered <c>catch (Exception)</c> turns every failure into whatever the
/// handler returns - usually empty. In this library that changed answers: an invalid regex made a negated
/// invariant true for every resource, and a regex timeout, cancellation or stack-guard exception thrown
/// inside such a block would have been reported as "no match". A handler must name the exception it means,
/// or filter <c>catch (Exception)</c> with <c>when</c> (normally <c>!ExceptionFilters.IsCritical(ex)</c>).
/// </para>
/// <para>
/// The scan cannot judge whether a filter is <em>correct</em>, only that one was written; that is what
/// review is for. It runs over whole-file text with comment lines blanked, so <c>catch</c> and its
/// <c>{</c> on separate lines are still seen, and documentation that mentions a bare <c>catch</c> is not.
/// </para>
/// </remarks>
public class FhirPathCatchAllGuardTests
{
    private const string LibraryPath = "src/Core/Ignixa.FhirPath";

    private static readonly Regex[] CatchAllPatterns =
    [
        // catch { ... }
        new(@"\bcatch\s*\{", RegexOptions.Compiled | RegexOptions.CultureInvariant),

        // catch (Exception) { / catch (System.Exception ex) { - no 'when' between ')' and '{'.
        new(@"\bcatch\s*\(\s*(?:global::)?(?:System\s*\.\s*)?Exception(?:\s+\w+)?\s*\)\s*\{", RegexOptions.Compiled | RegexOptions.CultureInvariant),
    ];

    /// <summary>
    /// Catch-alls that are deliberate, with the reason. Keyed by path relative to <see cref="LibraryPath"/>;
    /// the value is how many the file may hold. An entry whose count no longer matches fails the scan, so the
    /// list cannot outlive the code it excuses.
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Allowlist = new(StringComparer.Ordinal)
    {
        ["Parser/FhirPathParser.cs"] = (2,
            "TryParse / TryParseToTree are the Try-pattern over Parse: their contract is to report any parse "
            + "failure as false with its message. Narrowing them to FormatException (and letting OOM and "
            + "cancellation through) is tracked separately; the file is owned by the parse-limit work."),
    };

    [Fact]
    public void GivenFhirPathSources_WhenScanned_ThenNoCatchAllOutsideTheAllowlist()
    {
        var root = Path.Combine(RepoRoot.Find(), LibraryPath);
        var sources = LibrarySources(root);
        sources.Count.ShouldBeGreaterThan(50,
            $"Found too few .cs files under {LibraryPath} for this scan to be meaningful; the scan path is probably wrong.");

        var found = sources
            .Select(file => (File: Relative(root, file), CatchAlls: FindCatchAlls(File.ReadAllText(file))))
            .Where(entry => entry.CatchAlls.Count > 0)
            .ToList();

        var offenders = found
            .Where(entry => !Allowlist.TryGetValue(entry.File, out var allowed) || allowed.Count != entry.CatchAlls.Count)
            .SelectMany(entry => entry.CatchAlls.Select(catchAll => $"{LibraryPath}/{entry.File}({catchAll.Line}): {catchAll.Text}"))
            .ToList();

        var staleEntries = Allowlist.Keys
            .Where(file => found.All(entry => !string.Equals(entry.File, file, StringComparison.Ordinal)))
            .ToList();

        offenders.ShouldBeEmpty(
            "A bare catch or an unfiltered catch (Exception) converts every failure - including cancellation, "
            + "memory exhaustion, stack exhaustion and regex timeouts - into the handler's result. Catch the "
            + "exception the handler means, or add a 'when' filter such as !ExceptionFilters.IsCritical(ex). "
            + "If a catch-all is genuinely intended, add it to the allowlist in this file with a reason."
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        staleEntries.ShouldBeEmpty("These allowlist entries no longer hold a catch-all; remove them.");
    }

    /// <summary>
    /// The positive control: without it a defect in the matcher would let the scan report green over files
    /// it never really examined.
    /// </summary>
    [Theory]
    [InlineData("try { x(); } catch { return []; }")]
    [InlineData("try { x(); }\n        catch\n        {\n            return [];\n        }")]
    [InlineData("catch (Exception) { }")]
    [InlineData("catch (Exception ex)\n{\n    Log(ex);\n}")]
    [InlineData("catch (System.Exception ex) { }")]
    [InlineData("catch (global::System.Exception ex) { }")]
    public void GivenACatchAll_WhenScanned_ThenItIsDetected(string source)
    {
        FindCatchAlls(source).ShouldNotBeEmpty("The scan does not see this catch-all, so it is inert for code written this way.");
    }

    /// <summary>
    /// The negative control: typed and filtered handlers, and documentation, are not catch-alls.
    /// </summary>
    [Theory]
    [InlineData("catch (FormatException) { return []; }")]
    [InlineData("catch (ArgumentException ex) { throw new X(ex); }")]
    [InlineData("catch (Exception ex) when (!ExceptionFilters.IsCritical(ex)) { }")]
    [InlineData("catch (Exception ex)\n    when (ex is ArgumentException or InvalidCastException)\n{ }")]
    [InlineData("catch (ExceptionWrapper ex) { }")]
    [InlineData("/// The old bare <c>catch</c> folded overflow into empty: catch { return []; }")]
    [InlineData("// catch (Exception ex) { } was removed")]
    public void GivenAHandlerThatIsNotACatchAll_WhenScanned_ThenItIsNotReported(string source)
    {
        FindCatchAlls(source).ShouldBeEmpty("The scan reports a handler that names or filters its exception.");
    }

    private static IReadOnlyList<(int Line, string Text)> FindCatchAlls(string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var scannable = string.Join(
            '\n',
            lines.Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? string.Empty : line));

        return CatchAllPatterns
            .SelectMany(pattern => pattern.Matches(scannable).Cast<Match>())
            .Select(match => LineAt(scannable, lines, match.Index))
            .DistinctBy(catchAll => catchAll.Line)
            .OrderBy(catchAll => catchAll.Line)
            .ToList();
    }

    private static (int Line, string Text) LineAt(string scannable, string[] lines, int index)
    {
        int line = scannable.Take(index).Count(character => character == '\n');
        return (line + 1, lines[line].Trim());
    }

    private static List<string> LibrarySources(string root) =>
        [.. Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
}
