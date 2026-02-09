using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Utility;

namespace Ignixa.Anonymizer.Processors;

public class CryptoHashProcessor : IAnonymizerProcessor
{
    private readonly string _cryptoHashKey;
    private readonly IFhirSchemaProvider _schema;
    private readonly Func<string, string> _cryptoHashFunction;
    private readonly ILogger _logger = AnonymizerLogging.CreateLogger<CryptoHashProcessor>();

    public CryptoHashProcessor(string cryptoHashKey, IFhirSchemaProvider schema)
    {
        _cryptoHashKey = cryptoHashKey;
        _schema = schema;
        _cryptoHashFunction = input => CryptoHashUtility.ComputeHmacSHA256Hash(input, _cryptoHashKey);
    }

    public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
    {
        var processResult = new ProcessResult();
        if (string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
        }

        var input = node.Value.ToString()!;

        if (node.IsReferenceStringNode(parent: null))
        {
            var newReference = ReferenceUtility.TransformReferenceId(input, _schema, _cryptoHashFunction);
            ElementMutationHelper.SetValue(node, newReference);
        }
        else
        {
            ElementMutationHelper.SetValue(node, _cryptoHashFunction(input));
        }

        _logger.LogDebug("Fhir value '{Input}' at '{Location}' is hashed to '{Value}'.", input, node.Location, node.Value);

        processResult.AddProcessRecord(AnonymizationOperations.CryptoHash, node);
        return processResult;
    }
}
