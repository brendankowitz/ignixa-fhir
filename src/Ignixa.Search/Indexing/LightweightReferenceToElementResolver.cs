// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using ISourceNode = Ignixa.SourceNodeSerialization.Abstractions.ISourceNode;
using ITypedElement = Ignixa.SourceNodeSerialization.Abstractions.ITypedElement;
using Ignixa.Specification;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.SourceNodeSerialization.SourceNodes;
// For ToTypedElement extension method

// For ResourceJsonNode

namespace Ignixa.Search.Indexing;

/// <summary>
/// This class implements Resolve functionality that can be used in FHIR Path expressions such as
/// Encounter.participant.individual.where(resolve() is Practitioner)
/// In this case the "ResourceReference" is parsed into a type (the resolve)
/// which can then be type checked against a FHIR Resource.
/// Lightweight infers the types are created with minimal effort and with partial data.
/// </summary>
public class LightweightReferenceToElementResolver : IReferenceToElementResolver
{
    private readonly IReferenceSearchValueParser _referenceParser;
    private readonly IFhirSchemaProvider _schemaProvider;

    public LightweightReferenceToElementResolver(
        IReferenceSearchValueParser referenceParser,
        IFhirSchemaProvider schemaProvider)
    {
        EnsureArg.IsNotNull(referenceParser, nameof(referenceParser));
        EnsureArg.IsNotNull(schemaProvider, nameof(schemaProvider));

        _referenceParser = referenceParser;
        _schemaProvider = schemaProvider;
    }

    public ITypedElement Resolve(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        ReferenceSearchValue parsed = _referenceParser.Parse(reference);

        if (parsed == null) return null;

        // Create a minimal FHIR resource with just resourceType and id
        string json = $"{{\"resourceType\":\"{parsed.ResourceType}\",\"id\":\"{parsed.ResourceId}\"}}";
        ISourceNode node = ResourceJsonNode.Parse(json).ToSourceNode();

        return node.ToTypedElement(_schemaProvider);
    }
}
