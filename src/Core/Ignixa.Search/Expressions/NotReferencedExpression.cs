// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using EnsureThat;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents a _not-referenced expression that finds orphaned resources (resources not referenced by any other resource).
/// </summary>
/// <remarks>
/// Supports three patterns:
/// - *:* - Not referenced by any resource via any reference path
/// - {ResourceType}:* - Not referenced by the specified resource type
/// - {ResourceType}:{path} - Not referenced via the specific reference path
/// </remarks>
public class NotReferencedExpression : Expression
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotReferencedExpression"/> class.
    /// </summary>
    /// <param name="sourceResourceType">The resource type that might reference the target, or null for wildcard (*).</param>
    /// <param name="referencePath">The search parameter code for the reference path, or null for wildcard (*).</param>
    public NotReferencedExpression(string? sourceResourceType, string? referencePath)
    {
        SourceResourceType = sourceResourceType;
        ReferencePath = referencePath;
    }

    /// <summary>
    /// Gets the source resource type that might reference the target.
    /// Null indicates wildcard (*) - any resource type.
    /// </summary>
    public string? SourceResourceType { get; }

    /// <summary>
    /// Gets the reference path (search parameter code) through which the target might be referenced.
    /// Null indicates wildcard (*) - any reference path.
    /// </summary>
    public string? ReferencePath { get; }

    /// <summary>
    /// Gets or sets the resolved search parameter ID for the reference path.
    /// This is set during query generation when the path is specific (not wildcard).
    /// </summary>
    public short? SearchParamId { get; set; }

    /// <summary>
    /// Gets a value indicating whether this is a full wildcard expression (*:*).
    /// </summary>
    public bool IsFullWildcard => SourceResourceType is null && ReferencePath is null;

    /// <summary>
    /// Gets a value indicating whether the reference path is a wildcard (*).
    /// </summary>
    public bool IsPathWildcard => ReferencePath is null;

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));
        return visitor.VisitNotReferenced(this, context);
    }

    public override string ToString()
    {
        var source = SourceResourceType ?? "*";
        var path = ReferencePath ?? "*";
        return $"(NotReferenced {source}:{path})";
    }

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(NotReferencedExpression));
        hashCode.Add(SourceResourceType);
        hashCode.Add(ReferencePath);
    }

    public override bool ValueInsensitiveEquals(Expression other)
    {
        if (other is not NotReferencedExpression notReferenced)
        {
            return false;
        }

        return notReferenced.SourceResourceType == SourceResourceType
            && notReferenced.ReferencePath == ReferencePath;
    }
}
