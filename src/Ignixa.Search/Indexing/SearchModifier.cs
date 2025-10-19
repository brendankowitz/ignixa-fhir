// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Defines a search modifier applied to search parameter
/// </summary>
public sealed class SearchModifier : IEquatable<SearchModifier>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchModifier"/> class.
    /// </summary>
    /// <param name="searchModifierCode"><see cref="SearchModifierCode"/></param>
    /// <param name="resourceType">Resource type used to constrain abstract modifier code (e.g. <see cref="SearchModifierCode.Type"/>)</param>
    public SearchModifier(SearchModifierCode searchModifierCode, string resourceType = null)
    {
        if (searchModifierCode == SearchModifierCode.Type)
            EnsureArg.IsNotEmptyOrWhiteSpace(resourceType, nameof(resourceType));
        else
            EnsureArg.Is(resourceType, null, nameof(resourceType));

        SearchModifierCode = searchModifierCode;
        ResourceType = resourceType;
    }

    /// <summary>
    /// <see cref="SearchModifierCode"/>
    /// </summary>
    public SearchModifierCode SearchModifierCode { get; }

    /// <summary>
    /// Resource type used to constrain abstract modifier code (e.g. <see cref="SearchModifierCode.Type"/>)
    /// </summary>
    public string ResourceType { get; }

    public bool Equals([AllowNull] SearchModifier other)
    {
        return !(other is null) &&
               SearchModifierCode == other.SearchModifierCode &&
               string.Equals(ResourceType, other.ResourceType, StringComparison.OrdinalIgnoreCase);
    }

    public static bool operator ==(SearchModifier lhs, SearchModifier rhs)
    {
        return (object)lhs == rhs || (lhs?.Equals(rhs) ?? false);
    }

    public static bool operator !=(SearchModifier lhs, SearchModifier rhs)
    {
        return !(lhs == rhs);
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as SearchModifier);
    }

    public override int GetHashCode()
    {
        int h1 = SearchModifierCode.GetHashCode();
        int h2 = ResourceType?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
        return ((h1 << 5) + h1) ^ h2;
    }

    public override string ToString()
    {
        return SearchModifierCode == SearchModifierCode.Type ? ResourceType : SearchModifierCode.ToString();
    }
}
