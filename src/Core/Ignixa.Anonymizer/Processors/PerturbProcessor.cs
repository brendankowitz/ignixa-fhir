// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using MathNet.Numerics.Distributions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors.Settings;
using Ignixa.Anonymizer.Utility;

namespace Ignixa.Anonymizer.Processors;

public class PerturbProcessor : IAnonymizerProcessor
{
    private readonly HashSet<string> _quantityTypeNames;

    private static readonly HashSet<string> PrimitiveValueTypeNames = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "decimal",
        "integer",
        "positiveInt",
        "unsignedInt"
    };

    private static readonly HashSet<string> IntegerValueTypeNames = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "integer",
        "positiveInt",
        "unsignedInt"
    };

    // All quantity-like types across FHIR versions. Schema filters to those that exist.
    private static readonly string[] AllQuantityTypeNames =
    [
        "Age", "Count", "Duration", "Distance", "Money", "MoneyQuantity", "Quantity", "SimpleQuantity"
    ];

    public PerturbProcessor(ISchema schema)
    {
        _quantityTypeNames = new HashSet<string>(
            AllQuantityTypeNames.Where(t => schema.IsKnownType(t)),
            StringComparer.InvariantCultureIgnoreCase);
    }

    public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
    {
        EnsureArg.IsNotNull(node);
        EnsureArg.IsNotNull(context?.VisitedNodes);
        EnsureArg.IsNotNull(settings);

        var result = new ProcessResult();

        IElement? valueNode = null;
        if (PrimitiveValueTypeNames.Contains(node.InstanceType))
        {
            valueNode = node;
        }
        else if (_quantityTypeNames.Contains(node.InstanceType))
        {
            valueNode = node.Children(Constants.ValueNodeName).FirstOrDefault();
        }

        if (valueNode?.Value is null || context.VisitedNodes.Contains(valueNode.Location))
        {
            return result;
        }

        var perturbSetting = PerturbSetting.CreateFromRuleSettings(settings);

        AddNoise(valueNode, perturbSetting);
        foreach (var d in node.Descendants())
        {
            context.VisitedNodes.Add(d.Location);
        }
        result.AddProcessRecord(AnonymizationOperations.Perturb, node);
        return result;
    }

    private static void AddNoise(IElement node, PerturbSetting perturbSetting)
    {
        if (IntegerValueTypeNames.Contains(node.InstanceType))
        {
            perturbSetting.RoundTo = 0;
        }

        var originValue = decimal.Parse(node.Value!.ToString()!);
        var span = perturbSetting.Span;
        if (perturbSetting.RangeType == PerturbRangeType.Proportional)
        {
            span = (double)originValue * perturbSetting.Span;
        }

        var noise = (decimal)ContinuousUniform.Sample(-1 * span / 2, span / 2);
        var perturbedValue = decimal.Round(originValue + noise, perturbSetting.RoundTo);

        if (perturbedValue <= 0 && string.Equals("positiveInt", node.InstanceType, StringComparison.InvariantCultureIgnoreCase))
        {
            perturbedValue = 1;
        }
        if (perturbedValue < 0 && string.Equals("unsignedInt", node.InstanceType, StringComparison.InvariantCultureIgnoreCase))
        {
            perturbedValue = 0;
        }

        ElementMutationHelper.SetValue(node, perturbedValue);
    }
}
