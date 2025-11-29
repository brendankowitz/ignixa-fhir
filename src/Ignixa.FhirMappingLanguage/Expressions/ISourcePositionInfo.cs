/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Source position information for expressions.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Source position information for expressions.
/// </summary>
public interface ISourcePositionInfo
{
    int LineNumber { get; }
    int LinePosition { get; }
    int RawPosition { get; }
    int Length { get; }
}
