/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents indexer access in a FhirPath expression.
/// Examples: name[0], collection[5]
/// </summary>
public class IndexerExpression : FunctionCallExpression
{
    public IndexerExpression(Expression collection, Expression index, ISourcePositionInfo? location = null)
        : base(collection, "builtin.item", new[] { index }, location)
    {
    }

    public Expression Collection => Focus!;
    public Expression Index => Arguments[0];

    public override string ToString() => $"{Collection}[{Index}]";
}
