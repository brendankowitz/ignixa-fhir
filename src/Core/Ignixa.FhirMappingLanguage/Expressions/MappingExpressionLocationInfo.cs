/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Implementation of source position information.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Implementation of source position information.
/// </summary>
public class MappingExpressionLocationInfo : ISourcePositionInfo
{
    public int LineNumber { get; set; }
    public int LinePosition { get; set; }
    public int RawPosition { get; set; }
    public int Length { get; set; }
}
