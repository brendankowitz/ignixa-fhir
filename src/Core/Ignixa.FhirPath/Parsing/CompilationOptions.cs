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
public sealed class CompilationOptions
{
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
