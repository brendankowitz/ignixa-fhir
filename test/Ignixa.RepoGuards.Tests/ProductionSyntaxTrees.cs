// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Every C# file under <c>src/</c>, parsed once per test run and shared by the guards that scan production syntax.
/// </summary>
/// <remarks>
/// Parsing the whole tree takes seconds; each guard parsing it again was most of the suite's run time. Parsed in
/// parallel, and only on first use.
/// </remarks>
internal static class ProductionSyntaxTrees
{
    private static readonly Lazy<IReadOnlyList<(string RelativePath, SyntaxTree Tree)>> Trees = new(Parse);

    /// <summary>The <c>src/</c> directory the trees were read from.</summary>
    public static string Root { get; } = Path.Combine(RepoRoot.Find(), "src");

    /// <summary>Each file's path relative to <see cref="Root"/>, with forward slashes, and its syntax tree.</summary>
    public static IReadOnlyList<(string RelativePath, SyntaxTree Tree)> All => Trees.Value;

    private static IReadOnlyList<(string RelativePath, SyntaxTree Tree)> Parse()
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory
            .EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .AsParallel()
            .AsOrdered()
            .Select(file => (
                Path.GetRelativePath(Root, file).Replace(separator, '/'),
                CSharpSyntaxTree.ParseText(File.ReadAllText(file))))
            .ToList();
    }
}
