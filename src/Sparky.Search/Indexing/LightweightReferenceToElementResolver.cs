// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using ISourceNode = Sparky.SourceNodeSerialization.ElementModel.ISourceNode;
using ITypedElement = Sparky.SourceNodeSerialization.ElementModel.ITypedElement;
using Sparky.Extensions.Schema;
using Sparky.Search.Indexing.SearchValues;
using Sparky.SourceNodeSerialization.ElementModel;
// For ToTypedElement extension method
using Sparky.SourceNodeSerialization.SourceNodes.Models; // For ResourceJsonNode

namespace Sparky.Search.Indexing;

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

    public LightweightReferenceToElementResolver(
        IReferenceSearchValueParser referenceParser)
    {
        EnsureArg.IsNotNull(referenceParser, nameof(referenceParser));

        _referenceParser = referenceParser;
    }

    public ITypedElement Resolve(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        ReferenceSearchValue parsed = _referenceParser.Parse(reference);

        if (parsed == null) return null;

        // Create a minimal FHIR resource with just resourceType and id
        string json = $"{{\"resourceType\":\"{parsed.ResourceType}\",\"id\":\"{parsed.ResourceId}\"}}";
        ISourceNode node = ResourceJsonNode.Parse(json).ToSourceNode();

        return node.ToTypedElement(InstanceInferredStructureDefinitionSummaryProvider.CreateFrom(node));
    }
}
