// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
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
/// or filter <c>catch (Exception)</c> with a <c>when</c> that excludes something (normally
/// <c>!ExceptionFilters.IsCritical(ex)</c>). A filter that excludes nothing - <c>when (true)</c>,
/// <c>when (ex is not null)</c> - is a catch-all and is reported as one.
/// </para>
/// <para>
/// The scan cannot judge whether a filter is <em>correct</em>, only that one was written; that is what
/// review is for. Comments are blanked (string literals are left alone, so a <c>"//"</c> in a URL does not
/// hide the code after it) and matching runs over whole-file text, so a comment between <c>catch</c> and its
/// <c>{</c>, or a line break there, cannot hide a catch-all.
/// </para>
/// </remarks>
public class FhirPathCatchAllGuardTests
{
    private const string LibraryPath = "src/Core/Ignixa.FhirPath";

    private const string ExceptionType = @"(?:global::)?(?:System\s*\.\s*)?Exception";

    private static readonly Regex[] CatchAllPatterns =
    [
        // catch { ... }
        new(@"\bcatch\s*\{", RegexOptions.Compiled | RegexOptions.CultureInvariant),

        // catch (Exception) { / catch (System.Exception ex) { - no 'when' between ')' and '{'.
        new($@"\bcatch\s*\(\s*{ExceptionType}(?:\s+\w+)?\s*\)\s*\{{", RegexOptions.Compiled | RegexOptions.CultureInvariant),

        // catch (Exception ex) when (true) / when (ex is not null) / when (ex != null) - a filter that excludes nothing.
        new(
            $@"\bcatch\s*\(\s*{ExceptionType}(?:\s+(?<name>\w+))?\s*\)\s*when\s*\(\s*(?:true|\k<name>\s*(?:is\s+not\s+null|!=\s*null))\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),
    ];

    [Fact]
    public void GivenFhirPathSources_WhenScanned_ThenNoneHoldsACatchAll()
    {
        var root = Path.Combine(RepoRoot.Find(), LibraryPath);
        var sources = LibrarySources(root);
        sources.Count.ShouldBeGreaterThan(50,
            $"Found too few .cs files under {LibraryPath} for this scan to be meaningful; the scan path is probably wrong.");

        var offenders = sources
            .SelectMany(file => FindCatchAlls(File.ReadAllText(file))
                .Select(catchAll => $"{LibraryPath}/{Relative(root, file)}({catchAll.Line}): {catchAll.Text}"))
            .ToList();

        offenders.ShouldBeEmpty(
            "A bare catch or an unfiltered catch (Exception) converts every failure - including cancellation, "
            + "memory exhaustion, stack exhaustion and regex timeouts - into the handler's result. Catch the "
            + "exception the handler means, or add a 'when' filter such as !ExceptionFilters.IsCritical(ex)."
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The positive control: without it a defect in the matcher would let the scan report green over files
    /// it never really examined. The comment rows defeated an earlier revision that blanked only whole
    /// comment lines.
    /// </summary>
    [Theory]
    [InlineData("try { x(); } catch { return []; }")]
    [InlineData("try { x(); }\n        catch\n        {\n            return [];\n        }")]
    [InlineData("catch (Exception) { }")]
    [InlineData("catch (Exception ex)\n{\n    Log(ex);\n}")]
    [InlineData("catch (System.Exception ex) { }")]
    [InlineData("catch (global::System.Exception ex) { }")]
    [InlineData("catch (Exception ex) // swallow\n{ }")]
    [InlineData("catch (Exception ex) /* swallow */ { }")]
    [InlineData("catch /* all */ { }")]
    [InlineData("catch // all\n{ }")]
    [InlineData("catch (Exception ex) when (true) { }")]
    [InlineData("catch (Exception ex) when (ex is not null) { }")]
    [InlineData("catch (Exception e) when ( e != null ) { }")]
    [InlineData("var url = \"http://example.org\"; try { } catch { }")]
    public void GivenACatchAll_WhenScanned_ThenItIsDetected(string source)
    {
        FindCatchAlls(source).ShouldNotBeEmpty("The scan does not see this catch-all, so it is inert for code written this way.");
    }

    /// <summary>
    /// The negative control: typed and genuinely filtered handlers, comments and string literals are not catch-alls.
    /// </summary>
    [Theory]
    [InlineData("catch (FormatException) { return []; }")]
    [InlineData("catch (ArgumentException ex) { throw new X(ex); }")]
    [InlineData("catch (Exception ex) when (!ExceptionFilters.IsCritical(ex)) { }")]
    [InlineData("catch (Exception ex)\n    when (ex is ArgumentException or InvalidCastException)\n{ }")]
    [InlineData("catch (ExceptionWrapper ex) { }")]
    [InlineData("/// The old bare <c>catch</c> folded overflow into empty: catch { return []; }")]
    [InlineData("// catch (Exception ex) { } was removed")]
    [InlineData("/* catch { } */ int x;")]
    [InlineData("var message = \"never write catch { } here\";")]
    public void GivenAHandlerThatIsNotACatchAll_WhenScanned_ThenItIsNotReported(string source)
    {
        FindCatchAlls(source).ShouldBeEmpty("The scan reports a handler that names or filters its exception.");
    }

    private static IReadOnlyList<(int Line, string Text)> FindCatchAlls(string source)
    {
        var normalized = source.ReplaceLineEndings("\n");
        var lines = normalized.Split('\n');
        var scannable = BlankCommentsAndStrings(normalized);

        return CatchAllPatterns
            .SelectMany(pattern => pattern.Matches(scannable).Cast<Match>())
            .Select(match => LineAt(scannable, lines, match.Index))
            .DistinctBy(catchAll => catchAll.Line)
            .OrderBy(catchAll => catchAll.Line)
            .ToList();
    }

    /// <summary>
    /// Replaces the contents of comments and string/char literals with spaces, keeping newlines so line
    /// numbers still address the original text. Literals are tracked only so their contents can neither
    /// open a comment nor be mistaken for code; interpolation holes are treated as literal text, which is
    /// conservative for this scan.
    /// </summary>
    private static string BlankCommentsAndStrings(string source)
    {
        var output = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            if (StartsWith(source, i, "//"))
            {
                i = Blank(source, output, i, source.IndexOf('\n', i) is var end and >= 0 ? end : source.Length);
            }
            else if (StartsWith(source, i, "/*"))
            {
                var close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = Blank(source, output, i, close >= 0 ? close + 2 : source.Length);
            }
            else if (source[i] is '"' or '\'')
            {
                i = Blank(source, output, i, EndOfLiteral(source, i));
            }
            else
            {
                output.Append(source[i]);
                i++;
            }
        }

        return output.ToString();
    }

    private static int EndOfLiteral(string source, int start)
    {
        var quote = source[start];
        var verbatim = quote == '"' && start > 0 && source[start - 1] == '@';
        if (quote == '"' && StartsWith(source, start, "\"\"\""))
        {
            var close = source.IndexOf("\"\"\"", start + 3, StringComparison.Ordinal);
            return close >= 0 ? close + 3 : source.Length;
        }

        for (var i = start + 1; i < source.Length; i++)
        {
            if (!verbatim && source[i] == '\\')
            {
                i++;
            }
            else if (source[i] == quote)
            {
                if (verbatim && i + 1 < source.Length && source[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                return i + 1;
            }
        }

        return source.Length;
    }

    private static int Blank(string source, StringBuilder output, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            output.Append(source[i] == '\n' ? '\n' : ' ');
        }

        return end;
    }

    private static bool StartsWith(string source, int index, string value) =>
        string.CompareOrdinal(source, index, value, 0, value.Length) == 0;

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
