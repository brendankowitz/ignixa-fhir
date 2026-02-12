// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Configuration.ProcessorSettings;
using Ignixa.Anonymizer.Tools;

namespace Ignixa.Anonymizer.Processors;

/// <summary>
/// Processor that generalizes primitive values using FHIRPath case expressions and a configurable fallback operation.
/// </summary>
public partial class GeneralizeProcessor : IAnonymizerProcessor
{
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
                OperationType = AnonymizationOperations.Generalize,
                ProcessedPaths = wasModified ? [node.Location ?? string.Empty] : []
            };

            return ValueTask.FromResult(Result<ProcessorResult>.Success(newResult));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<ProcessorResult>.Failure(new AnonymizerError(
                "PROCESSOR_ERROR",
                $"Failed to process node: {ex.Message}",
                Exception: ex,
                Path: node.Location)));
        }
    }

    private static bool ProcessCore(IElement node, HashSet<string> visitedNodes, Dictionary<string, object> settings)
    {
        var isPrimitive = node.IsPrimitiveElement();
        if (!isPrimitive)
        {
            throw new RuleNotApplicableException(
                $"Generalization is not applicable on the node with type {node.InstanceType}. Only FHIR primitive nodes (ref: https://www.hl7.org/fhir/datatypes.html#primitive) are applicable.");
        }

        if (node.Value is null)
        {
            return false;
        }

        var generalizeSetting = GeneralizeSetting.CreateFromRuleSettings(settings);
        foreach (var eachCase in generalizeSetting.Cases)
        {
            try
            {
                if (node.Predicate(eachCase.Key))
                {
                    var newValue = node.Scalar(eachCase.Value.ToString()!);
                    ElementMutationTool.SetValue(node, newValue);
                    return true;
                }
            }
            catch (Exception ex)
            {
                throw new ProcessingException($"Generalize failed when processing {eachCase}.", ex);
            }
        }

        if (generalizeSetting.OtherValues == GeneralizationOtherValuesOperation.Redact)
        {
            ElementMutationTool.ClearValue(node);
        }

        return true;
    }
}
