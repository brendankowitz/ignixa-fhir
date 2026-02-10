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
using Ignixa.Anonymizer.Processors.Settings;
using Ignixa.Anonymizer.Utility;

namespace Ignixa.Anonymizer.Processors;

public partial class GeneralizeProcessor : IAnonymizerProcessor
{
    public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
    {
        EnsureArg.IsNotNull(node);
        EnsureArg.IsNotNull(context?.VisitedNodes);
        EnsureArg.IsNotNull(settings);

        var result = new ProcessResult();

        var isPrimitive = node.IsPrimitiveElement();
        if (!isPrimitive)
        {
            throw new AnonymizerRuleNotApplicableException(
                $"Generalization is not applicable on the node with type {node.InstanceType}. Only FHIR primitive nodes (ref: https://www.hl7.org/fhir/datatypes.html#primitive) are applicable.");
        }

        if (node.Value is null)
        {
            return result;
        }

        var generalizeSetting = GeneralizeSetting.CreateFromRuleSettings(settings);
        foreach (var eachCase in generalizeSetting.Cases)
        {
            try
            {
                if (node.Predicate(eachCase.Key))
                {
                    var newValue = node.Scalar(eachCase.Value.ToString()!);
                    ElementMutationHelper.SetValue(node, newValue);
                    result.AddProcessRecord(AnonymizationOperations.Generalize, node);
                    return result;
                }
            }
            catch (Exception ex)
            {
                throw new AnonymizerProcessingException($"Generalize failed when processing {eachCase}.", ex);
            }
        }

        if (generalizeSetting.OtherValues == GeneralizationOtherValuesOperation.Redact)
        {
            ElementMutationHelper.ClearValue(node);
        }

        result.AddProcessRecord(AnonymizationOperations.Generalize, node);
        return result;
    }
}
