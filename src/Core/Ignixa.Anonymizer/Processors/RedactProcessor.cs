// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Utility;

namespace Ignixa.Anonymizer.Processors;

public class RedactProcessor : IAnonymizerProcessor
{
    public bool EnablePartialDatesForRedact { get; set; }

    public bool EnablePartialAgesForRedact { get; set; }

    public bool EnablePartialZipCodesForRedact { get; set; }

    public List<string>? RestrictedZipCodeTabulationAreas { get; set; }

    public RedactProcessor(bool enablePartialDatesForRedact, bool enablePartialAgesForRedact, bool enablePartialZipCodesForRedact, List<string>? restrictedZipCodeTabulationAreas)
    {
        EnablePartialDatesForRedact = enablePartialDatesForRedact;
        EnablePartialAgesForRedact = enablePartialAgesForRedact;
        EnablePartialZipCodesForRedact = enablePartialZipCodesForRedact;
        RestrictedZipCodeTabulationAreas = restrictedZipCodeTabulationAreas;
    }

    public static RedactProcessor Create(AnonymizerConfigurationManager configurationManager)
    {
        var parameters = configurationManager.GetParameterConfiguration();
        return new RedactProcessor(
            parameters.EnablePartialDatesForRedact,
            parameters.EnablePartialAgesForRedact,
            parameters.EnablePartialZipCodesForRedact,
            parameters.RestrictedZipCodeTabulationAreas);
    }

    public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
    {
        if (string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return new ProcessResult();
        }

        if (node.IsDateNode())
        {
            return DateTimeUtility.RedactDateNode(node, EnablePartialDatesForRedact);
        }

        if (node.IsDateTimeNode() || node.IsInstantNode())
        {
            return DateTimeUtility.RedactDateTimeAndInstantNode(node, EnablePartialDatesForRedact);
        }

        if (node.IsAgeDecimalNode(parent: null))
        {
            return DateTimeUtility.RedactAgeDecimalNode(node, EnablePartialAgesForRedact);
        }

        if (node.IsPostalCodeNode())
        {
            return PostalCodeUtility.RedactPostalCode(node, EnablePartialZipCodesForRedact, RestrictedZipCodeTabulationAreas);
        }

        ElementMutationHelper.ClearValue(node);
        var result = new ProcessResult();
        result.AddProcessRecord(AnonymizationOperations.Redact, node);
        return result;
    }
}
