/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Compilation options for FhirPath parsing.
 * Controls optimization, validation, and debug output.
 */

namespace Ignixa.FhirPath.Parsing;

/// <summary>
/// Options controlling FhirPath compilation behavior.
/// </summary>
/// <remarks>
/// <para>
/// The three size limits exist because the parser, the AST builders, the analyzer, the delegate compiler
/// and the evaluator all recurse over the expression, and a .NET stack overflow cannot be caught: an
/// expression nested or chained deeply enough terminates the process. Expression text is routinely
/// untrusted (FHIRPath Patch paths, ViewDefinitions, package-supplied SearchParameters and invariants), so
/// the parser rejects oversized input with a <see cref="FormatException"/> before anything recurses over it.
/// </para>
/// <para>
/// The defaults sit below the smallest depths measured to exhaust a bare 1 MB thread stack (Windows x64,
/// .NET 10, Release, tier-1 JIT with dynamic PGO): about 25 levels when function-call and instance-selector
/// nesting alternate, and several hundred chained operators. Those depths move by tens of percent between
/// runs and JIT states, so they are an order of magnitude, not a guarantee. The defaults are also at least
/// 2.6 times above the deepest, most nested and longest expressions in the shipped search parameters and
/// invariants (tree depth 70, nesting 6, 3,107 characters).
/// </para>
/// <para>
/// The limits are the first line of defence, not the only one: every recursive stage, the parser's grammar
/// included, checks the remaining stack, so a thread that runs short anyway gets an exception rather than a
/// crash. Raising the limits makes that fallback, rather than a clean <see cref="FormatException"/>, the
/// normal outcome for oversized input.
/// </para>
/// </remarks>
public sealed class CompilationOptions
{
    /// <summary>Default for <see cref="MaxExpressionLength"/>.</summary>
    public const int DefaultMaxExpressionLength = 16_384;

    /// <summary>Default for <see cref="MaxNestingDepth"/>.</summary>
    public const int DefaultMaxNestingDepth = 16;

    /// <summary>Default for <see cref="MaxExpressionDepth"/>.</summary>
    public const int DefaultMaxExpressionDepth = 256;

    /// <summary>
    /// Enable optimizations during compilation (constant folding, etc.).
    /// Default: false (standard compilation)
    /// </summary>
    public bool Optimize { get; init; }

    /// <summary>
    /// Preserve whitespace and comments for round-tripping.
    /// Default: false (trivia is discarded)
    /// </summary>
    public bool PreserveTrivia { get; init; }

    /// <summary>
    /// The longest expression, in characters, the parser accepts.
    /// Default: <see cref="DefaultMaxExpressionLength"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxExpressionLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultMaxExpressionLength;

    /// <summary>
    /// The deepest nesting of brackets - <c>(</c>, <c>[</c> and <c>{</c>, which covers parenthesized
    /// expressions, function-call arguments, indexers and instance selectors - the parser accepts.
    /// Default: <see cref="DefaultMaxNestingDepth"/>.
    /// </summary>
    /// <remarks>
    /// A bracket level re-enters every precedence level of the grammar, so it costs the parser far more
    /// stack than a chained operator costs any later stage. That is why it has its own, much lower, limit
    /// than <see cref="MaxExpressionDepth"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxNestingDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultMaxNestingDepth;

    /// <summary>
    /// The deepest syntax tree the parser accepts, counting every level: each member access, operator,
    /// indexer and function call in a chain such as <c>a.b.c</c> or <c>1 + 2 + 3</c> adds one, as does
    /// each level of bracket nesting.
    /// Default: <see cref="DefaultMaxExpressionDepth"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxExpressionDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultMaxExpressionDepth;

    /// <summary>
    /// Default compilation options (no optimizations, no trivia preservation).
    /// </summary>
    public static CompilationOptions Default { get; } = new();

    /// <summary>
    /// Compilation options with optimizations enabled.
    /// </summary>
    public static CompilationOptions Optimized { get; } = new() { Optimize = true };

    /// <summary>
    /// Compilation options for debugging/round-tripping (preserves trivia).
    /// </summary>
    public static CompilationOptions Debug { get; } = new() { PreserveTrivia = true };
}
