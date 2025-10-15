// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Sparky.SourceNodeSerialization.ElementModel;
using Sparky.SourceNodeSerialization.Specification;
using ISourceNode = Sparky.SourceNodeSerialization.ElementModel.ISourceNode;

// For GetResourceTypeIndicator extension method

namespace Sparky.Extensions.Schema;

public class InstanceInferredStructureDefinitionSummaryProvider : IStructureDefinitionSummaryProvider
{
    private readonly ISourceNode _typedElement;

    private InstanceInferredStructureDefinitionSummaryProvider(ISourceNode typedElement)
    {
        _typedElement = typedElement;
    }

    public IStructureDefinitionSummary Provide(string canonical)
    {
        return new GenericStructureDefinitionSummary(_typedElement);
    }

    public static IStructureDefinitionSummaryProvider CreateFrom(ISourceNode typedElement)
    {
        return new InstanceInferredStructureDefinitionSummaryProvider(typedElement);
    }

    private class GenericStructureDefinitionSummary : IStructureDefinitionSummary
    {
        private readonly ISourceNode[] _typedElement;

        public GenericStructureDefinitionSummary(params ISourceNode[] typedElement)
        {
            _typedElement = typedElement;
        }

        public string TypeName => char.IsUpper(_typedElement[0].Name[0]) ? _typedElement[0].Name : null;

        public bool IsAbstract { get; }

        public bool IsResource => !string.IsNullOrEmpty(_typedElement[0].GetResourceTypeIndicator());

        public IReadOnlyCollection<IElementDefinitionSummary> GetElements()
        {
            var children = new List<IElementDefinitionSummary>();

            foreach ((IGrouping<string, ISourceNode> element, int i) tuple in _typedElement.SelectMany(x => x.Children()).GroupBy(x => x.Name).Select((element, i) => (element, i))) children.Add(new GenericElementDefinitionSummary(tuple.element.ToArray(), tuple.i));

            return children;
        }
    }

    private class GenericElementDefinitionSummary : IElementDefinitionSummary
    {
        private readonly ISourceNode[] _typedElement;

        public GenericElementDefinitionSummary(ISourceNode[] typedElement, int order)
        {
            _typedElement = typedElement;
            Order = order;
        }

        public string ElementName => _typedElement[0].Name;

        public bool IsCollection => _typedElement[0].Location.Contains('[', StringComparison.Ordinal);

        public bool IsRequired { get; }

        public bool InSummary { get; }

        public bool IsChoiceElement { get; }

        public bool IsResource { get; }

        public bool IsModifier { get; }

        public ITypeSerializationInfo[] Type => new ITypeSerializationInfo[] { new GenericStructureDefinitionSummary(_typedElement) };

        public string DefaultTypeName { get; }

        public string NonDefaultNamespace { get; }

        public XmlRepresentation Representation => XmlRepresentation.TypeAttr;

        public int Order { get; }
    }
}
