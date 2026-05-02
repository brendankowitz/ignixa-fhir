// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using MathNet.Numerics.Distributions;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.DeId.Configuration.ProcessorSettings;
using Ignixa.DeId.Tools;

namespace Ignixa.DeId.Processors;

/// <summary>
/// Processor that adds uniform random noise to numeric values (decimal, integer, or quantity types).
/// </summary>
public class PerturbProcessor : IDeIdProcessor
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

    private static readonly string[] AllQuantityTypeNames =
    [
        "Age", "Count", "Duration", "Distance", "Money", "MoneyQuantity", "Quantity", "SimpleQuantity"
    ];

    public PerturbProcessor(ISchema schema)
    {
        EnsureArg.IsNotNull(schema, nameof(schema));

        _quantityTypeNames = new HashSet<string>(
            AllQuantityTypeNames.Where(t => schema.IsKnownType(t)),
            StringComparer.InvariantCultureIgnoreCase);
    }

    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(node, nameof(node));
            EnsureArg.IsNotNull(context.VisitedNodes, nameof(context.VisitedNodes));
            EnsureArg.IsNotNull(context.Settings, nameof(context.Settings));

            var settings = context.Settings.ToDictionary(kv => kv.Key, kv => kv.Value);
            var wasModified = ProcessCore(node, context.VisitedNodes, settings);

            var newResult = new ProcessorResult
            {
                WasModified = wasModified,
                OperationType = DeIdOperations.Perturb,
                ProcessedPaths = wasModified ? [node.Location ?? string.Empty] : []
            };

            return ValueTask.FromResult(Result<ProcessorResult>.Success(newResult));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<ProcessorResult>.Failure(new DeIdError(
                "PROCESSOR_ERROR",
                $"Failed to process node: {ex.Message}",
                Exception: ex,
                Path: node.Location)));
        }
    }

    private bool ProcessCore(IElement node, HashSet<string> visitedNodes, Dictionary<string, object> settings)
    {
        IElement? valueNode = null;
        if (PrimitiveValueTypeNames.Contains(node.InstanceType))
        {
            valueNode = node;
        }
        else if (_quantityTypeNames.Contains(node.InstanceType))
        {
            valueNode = node.Children(Constants.ValueNodeName).FirstOrDefault();
        }

        if (valueNode?.Value is null || visitedNodes.Contains(valueNode.Location))
        {
            return false;
        }

        var perturbSetting = PerturbSetting.CreateFromRuleSettings(settings);

        AddNoise(valueNode, perturbSetting);
        foreach (var d in node.Descendants())
        {
            visitedNodes.Add(d.Location);
        }

        return true;
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

        ElementMutationTool.SetValue(node, perturbedValue);
    }
}
