// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Data;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Visitors;

public class AnonymizationVisitor : AbstractElementNodeVisitor
{
    private readonly ResourceProcessor _resourceProcessor;
    private readonly Stack<(IElement Node, ProcessResult Result)> _contextStack = new();

    public bool AddSecurityTag { get; set; } = true;

    public AnonymizationVisitor(AnonymizationFhirPathRule[] rules, Dictionary<string, IAnonymizerProcessor> processors)
    {
        _resourceProcessor = new ResourceProcessor(rules, processors);
    }

    public override bool Visit(ResourceJsonNode resource, IElement node)
    {
        if (node.IsFhirResource())
        {
            var result = _resourceProcessor.Process(resource, node);
            _contextStack.Push((node, result));
        }

        return true;
    }

    public override void EndVisit(ResourceJsonNode resource, IElement node)
    {
        if (node.IsFhirResource())
        {
            var context = _contextStack.Pop();
            var result = context.Result;

            if (context.Node != node)
            {
                throw new ConstraintException("Internal error: access wrong context.");
            }

            if (_contextStack.Count > 0)
            {
                _contextStack.Peek().Result.Update(result);
            }

            if (AddSecurityTag && !node.IsContainedNode())
            {
                _resourceProcessor.AddSecurityTag(resource, node, result);
            }
        }
    }
}
