using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Utility;

public class PostalCodeUtility
{
    private static readonly string ReplacementDigit = "0";
    private static readonly int InitialDigitsCount = 3;

    public static ProcessResult RedactPostalCode(IElement node, bool enablePartialZipCodesForRedact = false, List<string>? restrictedZipCodeTabulationAreas = null)
    {
        var processResult = new ProcessResult();
        if (!node.IsPostalCodeNode() || string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
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
            processResult.AddProcessRecord(AnonymizationOperations.Abstract, node);
        }
        else
        {
            ElementMutationHelper.ClearValue(node);
            processResult.AddProcessRecord(AnonymizationOperations.Redact, node);
        }

        return processResult;
    }
}
