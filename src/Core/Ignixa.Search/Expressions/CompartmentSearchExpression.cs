// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents an expression for search performed for a compartment.
/// Supports filtering to specific resource types (or wildcard for all types in compartment).
/// </summary>
public class CompartmentSearchExpression : Expression
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompartmentSearchExpression"/> class.
    /// </summary>
    /// <param name="compartmentType">The compartment type.</param>
    /// <param name="compartmentId">The compartment id.</param>
    /// <param name="filteredResourceTypes">Optional set of resource types to limit search to. If empty or null, searches all types in compartment.</param>
    public CompartmentSearchExpression(string compartmentType, string compartmentId, ISet<string> filteredResourceTypes = null)
    {
        EnsureArg.IsNotNullOrWhiteSpace(compartmentId, nameof(compartmentId));

        CompartmentType = compartmentType;
        CompartmentId = compartmentId;
        FilteredResourceTypes = filteredResourceTypes ?? new HashSet<string>();
    }

    /// <summary>
    /// The compartment type.
    /// </summary>
    public string CompartmentType { get; }

    /// <summary>
    /// The compartment id.
    /// </summary>
    public string CompartmentId { get; }

    /// <summary>
    /// Optional set of resource types to limit search to.
    /// If empty, all resource types in the compartment are included.
    /// Used for compartment wildcard searches (Patient/123/*) to expand to all types in single rewriter pass.
    /// </summary>
    public ISet<string> FilteredResourceTypes { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));
        return visitor.VisitCompartment(this, context);
    }

    public override string ToString()
    {
        return $"(Compartment {CompartmentType} '{CompartmentId}')";
    }

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(CompartmentSearchExpression));
        hashCode.Add(CompartmentType);
    }

    public override bool ValueInsensitiveEquals(Expression other)
    {
        return other is CompartmentSearchExpression compartmentSearch && compartmentSearch.CompartmentType == CompartmentType;
    }
}
