// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
/// The scan parses each file with Roslyn rather than matching text, so every interpolated literal form
/// (<c>$"..."</c>, <c>@$"..."</c>, <c>$@"..."</c>, single- and multi-line <c>$"""..."""</c>) is seen as one node,
/// wherever its lines break, and comments are never scanned. An interpolated literal is reported when it
/// (a) is an argument to <c>Select</c>, <c>Scalar</c>, <c>IsTrue</c>, <c>IsBoolean</c> or <c>Predicate</c>, or to
/// <c>Parse</c>, <c>Evaluate</c> or <c>Compile</c> on a FHIRPath parser/evaluator receiver, or (b) holds a
/// quoted hole (<c>'{x}'</c>) next to a FHIRPath function call. It is syntactic, with no type information: an
/// expression assembled by concatenation, or built into a variable that is interpolated later, is not seen.
/// </para>
/// </remarks>
public class FhirPathInterpolationGuardTests
{
    private const string SourcePath = "src";

    private static readonly HashSet<string> ExpressionArgumentMethods =
        new(["Select", "Scalar", "IsTrue", "IsBoolean", "Predicate"], StringComparer.Ordinal);

    private static readonly HashSet<string> ParserArgumentMethods =
        new(["Parse", "Evaluate", "Compile"], StringComparer.Ordinal);

    // The receiver of Parse/Evaluate/Compile must look like a FHIRPath type or field, so XDocument.Parse and
    // a query-string parameterParser.Parse are out of scope without an allowlist.
    private static readonly Regex FhirPathReceiver =
        new(@"fhirpath|^_?(?:parser|evaluator|compiler)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex QuotedHole =
        new(@"'\{+[^{}]+\}+'", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FhirPathFunctionCall =
        new(@"\.(?:where|exists|first|select|all|ofType|startsWith|endsWith|contains|extension|matches|iif)\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Interpolations that are not FHIRPath, with the reason. Keyed by the path relative to
    /// <see cref="SourcePath"/> and the whitespace-normalized literal text, so a second interpolation on the
    /// same line is a different key and cannot ride on an existing entry. An entry that no longer matches
    /// anything fails the scan, so the list cannot outlive the code it excuses.
    /// </summary>
    private static readonly Dictionary<(string File, string Literal), string> Allowlist = new();

    [Fact]
    public void GivenSourceFiles_WhenScanned_ThenNoValueIsInterpolatedIntoFhirPathOutsideTheAllowlist()
    {
        var root = Path.Combine(RepoRoot.Find(), SourcePath);
        var sources = Sources(root);
        sources.Count.ShouldBeGreaterThan(500,
            $"Found too few .cs files under {SourcePath} for this scan to be meaningful; the scan path is probably wrong.");

        var found = sources
            .SelectMany(file => FindInterpolations(File.ReadAllText(file))
                .Select(hit => (File: Relative(root, file), Hit: hit)))
            .ToList();

        var offenders = found
            .Where(entry => !Allowlist.ContainsKey((entry.File, entry.Hit.Literal)))
            .Select(entry => $"{SourcePath}/{entry.File}({entry.Hit.Line}): {entry.Hit.Literal}")
            .ToList();

        var staleEntries = Allowlist.Keys
            .Where(key => !found.Any(entry => entry.File == key.File && entry.Hit.Literal == key.Literal))
            .ToList();

        offenders.ShouldBeEmpty(
            "A value interpolated into FHIRPath text can change the meaning of the expression and fills the "
            + "expression cache with caller-chosen keys. Pass it as an environment variable (%name) through "
            + "EvaluationContext.WithEnvironmentVariable instead. If the interpolation is not FHIRPath, add it "
            + "to the allowlist in this file with a reason."
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        staleEntries.ShouldBeEmpty("These allowlist entries no longer match an interpolation; remove them.");
    }

    /// <summary>
    /// The positive control: without it a defect in the matcher would let the scan report green over files
    /// it never really examined. Covers each literal form, and a literal split across lines.
    /// </summary>
    [Theory]
    [InlineData("""class C { string e = $"rest.resource.where(type = '{ResourceType}').exists()"; }""")]
    [InlineData("""class C { string e = @$"rest.resource.where(type = '{ResourceType}').exists()"; }""")]
    [InlineData("""class C { string e = $@"rest.resource.where(type = '{ResourceType}').exists()"; }""")]
    [InlineData("class C { string e = $\"\"\"rest.resource.where(type = '{ResourceType}').exists()\"\"\"; }")]
    [InlineData("class C { string e = $\"\"\"\n    rest.resource\n        .where(type = '{ResourceType}')\n    \"\"\"; }")]
    [InlineData("class C { string e = $$\"\"\"rest.resource.where(type = '{{ResourceType}}').exists()\"\"\"; }")]
    [InlineData("""class C { void M() { x.Select($"snapshot.element.where(path='{path}')"); } }""")]
    [InlineData("""class C { void M() { element.Scalar($"{path}.fixedString"); } }""")]
    [InlineData("""class C { void M() { element.IsTrue( $"{expression}" ); } }""")]
    [InlineData("class C { void M() { element.Scalar(\n    $\"\"\"\n    {path}.fixedString\n    \"\"\"); } }")]
    [InlineData("""class C { void M() { _parser.Parse($"{expression}"); } }""")]
    [InlineData("""class C { void M() { fhirPathEvaluator.Evaluate($"{expression}"); } }""")]
    public void GivenAnInterpolatedFhirPathExpression_WhenScanned_ThenItIsDetected(string source)
    {
        FindInterpolations(source).ShouldNotBeEmpty("The scan does not see this interpolation, so it is inert for code written this way.");
    }

    /// <summary>
    /// The negative control: constant expressions, variable bindings, ordinary messages, other Parse calls
    /// and comments are not reported.
    /// </summary>
    [Theory]
    [InlineData("""class C { string e = "rest.resource.where(type = %resourceType).exists()"; }""")]
    [InlineData("""class C { void M() { x.Select("snapshot.element.where(path = %path)", context); } }""")]
    [InlineData("""class C { void M() { throw new BadRequestException($"Resource type '{resourceType}' is not supported"); } }""")]
    [InlineData("""class C { string e = $"{baseName}.ofType({variant})"; }""")]
    [InlineData("""class C { void M() { XDocument.Parse($"<root>{xhtml}</root>"); } }""")]
    [InlineData("""class C { void M() { parameterParser.Parse($"_id={id}"); } }""")]
    [InlineData("""class C { void M() { items.Select(item => $"{item}"); } }""")]
    [InlineData("""// var e = $"rest.resource.where(type = '{ResourceType}')";""")]
    public void GivenCodeWithoutInterpolatedFhirPath_WhenScanned_ThenItIsNotReported(string source)
    {
        FindInterpolations(source).ShouldBeEmpty("The scan reports text that does not interpolate a value into FHIRPath.");
    }

    private static IReadOnlyList<(int Line, string Literal)> FindInterpolations(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);

        return tree.GetRoot()
            .DescendantNodes()
            .OfType<InterpolatedStringExpressionSyntax>()
            .Where(IsFhirPathInterpolation)
            .Select(node => (
                Line: node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                Literal: Regex.Replace(node.ToString(), @"\s+", " ")))
            .ToList();
    }

    private static bool IsFhirPathInterpolation(InterpolatedStringExpressionSyntax literal)
    {
        var text = literal.ToString();
        if (QuotedHole.IsMatch(text) && FhirPathFunctionCall.IsMatch(text))
        {
            return true;
        }

        return literal.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } }
            && TakesFhirPathExpression(call);
    }

    private static bool TakesFhirPathExpression(InvocationExpressionSyntax call)
    {
        switch (call.Expression)
        {
            case MemberAccessExpressionSyntax member:
                var name = member.Name.Identifier.ValueText;
                return ExpressionArgumentMethods.Contains(name)
                    || (ParserArgumentMethods.Contains(name) && FhirPathReceiver.IsMatch(member.Expression.ToString()));
            case IdentifierNameSyntax identifier:
                return ExpressionArgumentMethods.Contains(identifier.Identifier.ValueText);
            default:
                return false;
        }
    }

    private static List<string> Sources(string root) =>
        [.. Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
}
