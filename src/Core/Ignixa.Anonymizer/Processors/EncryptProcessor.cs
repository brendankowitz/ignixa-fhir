// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Utility;

namespace Ignixa.Anonymizer.Processors;

public class EncryptProcessor : IAnonymizerProcessor
{
    private readonly byte[] _key;
    private readonly ILogger _logger = AnonymizerLogging.CreateLogger<EncryptProcessor>();

    public EncryptProcessor(string encryptKey)
    {
        EnsureArg.IsNotNullOrWhiteSpace(encryptKey, nameof(encryptKey));

        _key = Encoding.UTF8.GetBytes(encryptKey);
    }

    public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
    {
        var processResult = new ProcessResult();
        if (string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
        }

        var input = node.Value.ToString()!;
        ElementMutationHelper.SetValue(node, EncryptUtility.EncryptTextToBase64WithAes(input, _key));
        _logger.LogDebug("Fhir value '{Input}' at '{Location}' is encrypted to '{Value}'.", input, node.Location, node.Value);

        processResult.AddProcessRecord(AnonymizationOperations.Encrypt, node);
        return processResult;
    }
}
