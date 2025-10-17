/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Sparky Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Position information for a source element in a parsed expression.
/// </summary>
public interface ISourcePositionInfo
{
    /// <summary>Line number (1-based)</summary>
    int LineNumber { get; }

    /// <summary>Column position within the line (1-based)</summary>
    int LinePosition { get; }

    /// <summary>Absolute character position from start (0-based)</summary>
    int RawPosition { get; }

    /// <summary>Length of the element in characters</summary>
    int Length { get; }
}

/// <summary>
/// Implementation of position information for FhirPath expressions.
/// </summary>
public class FhirPathExpressionLocationInfo : ISourcePositionInfo
{
    public int LineNumber { get; set; }
    public int LinePosition { get; set; }
    public int RawPosition { get; set; }
    public int Length { get; set; }

    public override string ToString() =>
        $"Line {LineNumber}, Column {LinePosition}, Position {RawPosition}, Length {Length}";
}
