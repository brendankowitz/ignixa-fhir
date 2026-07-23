// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using EnsureThat;

namespace Ignixa.Search.Indexing.SearchValues;

/// <summary>
/// Represents a reference search value.
/// </summary>
public class ReferenceSearchValue : ISearchValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReferenceSearchValue"/> class.
    /// </summary>
    /// <param name="referenceKind">The kind of reference.</param>
    /// <param name="baseUri">The base URI of the resource.</param>
    /// <param name="resourceType">The resource type.</param>
    /// <param name="resourceId">The resource id.</param>
    /// <remarks>
    /// <paramref name="referenceKind"/> and <paramref name="baseUri"/> are two views of the same fact, so
    /// they are required to agree: <see cref="ReferenceKind.Internal"/> means "this server", which carries
    /// no base, and <see cref="ReferenceKind.External"/> means "that server", which is meaningless without
    /// one. <see cref="ReferenceKind.InternalOrExternal"/> is the only kind that permits either. Consumers
    /// branch on one or the other to build their SQL, so a disagreeing pair makes two consumers reach
    /// opposite conclusions from the same value, widening or narrowing a search with no diagnostic.
    /// </remarks>
    public ReferenceSearchValue(ReferenceKind referenceKind, Uri baseUri, string resourceType, string resourceId)
    {
        if (baseUri != null) EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

        EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));

        if (referenceKind == ReferenceKind.Internal && baseUri != null)
        {
            throw new ArgumentException(
                $"A {nameof(ReferenceKind.Internal)} reference resolves to this server and must not carry a base URI, but '{baseUri}' was supplied.",
                nameof(baseUri));
        }

        if (referenceKind == ReferenceKind.External && baseUri == null)
        {
            throw new ArgumentException(
                $"An {nameof(ReferenceKind.External)} reference must carry the base URI of the server it points at.",
                nameof(baseUri));
        }

        Kind = referenceKind;
        BaseUri = baseUri;
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    /// <summary>
    /// Gets the kind of reference.
    /// </summary>
    public ReferenceKind Kind { get; }

    /// <summary>
    /// Gets the base URI of the resource.
    /// </summary>
    public Uri BaseUri { get; }

    /// <summary>
    /// Gets the resource type.
    /// </summary>
    public string ResourceType { get; }

    /// <summary>
    /// Gets the resource id.
    /// </summary>
    public string ResourceId { get; }

    /// <inheritdoc />
    public bool IsValidAsCompositeComponent => true;

    /// <inheritdoc />
    public void AcceptVisitor(ISearchValueVisitor visitor)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));

        visitor.Visit(this);
    }

    public bool Equals([AllowNull] ISearchValue other)
    {
        if (other == null) return false;

        var referenceSearchValueOther = other as ReferenceSearchValue;

        if (referenceSearchValueOther == null) return false;

        return Kind == referenceSearchValueOther.Kind &&
               BaseUri == referenceSearchValueOther.BaseUri &&
               ResourceType.Equals(referenceSearchValueOther.ResourceType, StringComparison.OrdinalIgnoreCase) &&
               ResourceId.Equals(referenceSearchValueOther.ResourceId, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (BaseUri != null)
            return $"{BaseUri}{ResourceType}/{ResourceId}";
        else if (string.IsNullOrWhiteSpace(ResourceType)) return ResourceId;

        return $"{ResourceType}/{ResourceId}";
    }
}
