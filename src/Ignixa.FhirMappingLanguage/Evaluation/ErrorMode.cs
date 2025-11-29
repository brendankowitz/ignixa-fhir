/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Error mode for mapping execution.
 */

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Error mode for mapping execution.
/// </summary>
public enum ErrorMode
{
    /// <summary>
    /// Throw exceptions on errors (default behavior).
    /// </summary>
    Strict,

    /// <summary>
    /// Collect errors and continue execution where possible.
    /// </summary>
    Lenient
}
