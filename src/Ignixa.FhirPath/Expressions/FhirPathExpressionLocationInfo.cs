/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

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
