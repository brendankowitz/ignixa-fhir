// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Utility;

public static class PostalCodeUtility
{
    private static readonly string ReplacementDigit = "0";
    private static readonly int InitialDigitsCount = 3;

    public readonly record struct RedactResult(bool WasModified, string OperationType);

    public static RedactResult RedactPostalCode(IElement node, bool enablePartialZipCodesForRedact = false, List<string>? restrictedZipCodeTabulationAreas = null)
    {
        if (!node.IsPostalCodeNode() || string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return new RedactResult(false, AnonymizationOperations.Redact);
        }

        var valueStr = node.Value.ToString()!;

        if (enablePartialZipCodesForRedact)
        {
            if (restrictedZipCodeTabulationAreas is not null && restrictedZipCodeTabulationAreas.Any(x => valueStr.StartsWith(x)))
            {
                ElementMutationHelper.SetValue(node, Regex.Replace(valueStr, @"\d", ReplacementDigit));
            }
            else if (valueStr.Length >= InitialDigitsCount)
            {
                var suffix = valueStr[InitialDigitsCount..];
                ElementMutationHelper.SetValue(node, $"{valueStr[..InitialDigitsCount]}{Regex.Replace(suffix, @"\d", ReplacementDigit)}");
            }
            return new RedactResult(true, AnonymizationOperations.Abstract);
        }
        else
        {
            ElementMutationHelper.ClearValue(node);
            return new RedactResult(true, AnonymizationOperations.Redact);
        }
    }
}
