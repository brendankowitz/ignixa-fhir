// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Utility;

namespace Ignixa.Anonymizer.Processors;

public class DateShiftProcessor : IAnonymizerProcessor
{
    public string DateShiftKey { get; set; } = string.Empty;

    public string DateShiftKeyPrefix { get; set; } = string.Empty;

    public int? DateShiftFixedOffsetInDays { get; set; }

    public bool EnablePartialDatesForRedact { get; set; }

    public DateShiftProcessor(string dateShiftKey, string dateShiftKeyPrefix, bool enablePartialDatesForRedact, int? dateShiftFixedOffsetInDays = null)
    {
        DateShiftKey = dateShiftKey;
        DateShiftKeyPrefix = dateShiftKeyPrefix;
        EnablePartialDatesForRedact = enablePartialDatesForRedact;
        DateShiftFixedOffsetInDays = dateShiftFixedOffsetInDays;
    }

    public static DateShiftProcessor Create(AnonymizerConfigurationManager configurationManager)
    {
        var parameters = configurationManager.GetParameterConfiguration();
        return new DateShiftProcessor(
            parameters.DateShiftKey,
            parameters.DateShiftKeyPrefix,
            parameters.EnablePartialDatesForRedact,
            parameters.DateShiftFixedOffsetInDays ?? null);
    }

    public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
    {
        var processResult = new ProcessResult();
        if (string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
        }

        // Use the resource id from context as the per-resource date shift prefix.
        // In the old Firely SDK, TryGetResourceId walked up via Parent to find the enclosing resource.
        // Since IElement has no Parent, we get the resource id from ProcessContext instead.
        var effectivePrefix = DateShiftKeyPrefix;
        if (string.IsNullOrEmpty(effectivePrefix))
        {
            effectivePrefix = context?.ResourceId ?? string.Empty;
        }

        if (node.IsDateNode())
        {
            return DateTimeUtility.ShiftDateNode(node, DateShiftKey, effectivePrefix, DateShiftFixedOffsetInDays, EnablePartialDatesForRedact);
        }

        if (node.IsDateTimeNode() || node.IsInstantNode())
        {
            return DateTimeUtility.ShiftDateTimeAndInstantNode(node, DateShiftKey, effectivePrefix, DateShiftFixedOffsetInDays, EnablePartialDatesForRedact);
        }

        return processResult;
    }
}
