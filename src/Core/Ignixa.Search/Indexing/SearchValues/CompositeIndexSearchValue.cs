// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using EnsureThat;

namespace Ignixa.Search.Indexing.SearchValues;

/// <summary>
/// Represents a composite search-parameter value during indexing. Constructed only by
/// <see cref="ElementSearchIndexer"/> on the write path -- neither the legacy query parser
/// (<see cref="Ignixa.Search.Expressions.Parsers.Legacy.LegacySearchParameterExpressionParser"/>)
/// nor the current query parser (<see cref="Ignixa.Search.Expressions.Parsers.SearchExpressionBinder"/>)
/// ever construct or consume this type. Composite query-side handling decomposes into per-component
/// atomic values before any aggregate value exists -- see
/// docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md.
/// </summary>
public class CompositeIndexSearchValue : ISearchValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeIndexSearchValue"/> class.
    /// </summary>
    /// <param name="components">The composite component values.</param>
    public CompositeIndexSearchValue(IReadOnlyList<IReadOnlyList<ISearchValue>> components)
    {
        EnsureArg.IsNotNull(components, nameof(components));
        EnsureArg.HasItems(components, nameof(components));

        Components = components;
    }

    /// <summary>
    /// Gets the composite component values.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ISearchValue>> Components { get; }

    /// <inheritdoc />
    public bool IsValidAsCompositeComponent => false;

    /// <inheritdoc />
    public void AcceptVisitor(ISearchValueVisitor visitor)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));

        visitor.Visit(this);
    }

    public bool Equals([AllowNull] ISearchValue other)
    {
        if (other == null) return false;

        var compositeSearchValueOther = other as CompositeIndexSearchValue;

        if (compositeSearchValueOther == null) return false;

        return Components.SequenceEqual(compositeSearchValueOther.Components);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Join(" $ ", Components.Select(component => string.Join(", ", component.Select(v => $"({v})"))));
    }
}
