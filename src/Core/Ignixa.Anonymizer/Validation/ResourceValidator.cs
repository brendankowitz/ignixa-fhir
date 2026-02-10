// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Anonymizer.Validation;

public class ResourceValidator
{
    private readonly ILogger _logger = AnonymizerLogging.CreateLogger<ResourceValidator>();

    public void ValidateInput(string resourceJson)
    {
        ValidateJsonStructure(resourceJson, "input");
    }

    public void ValidateOutput(string resourceJson)
    {
        ValidateJsonStructure(resourceJson, "output");
    }

    private void ValidateJsonStructure(string json, string context)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                throw new ResourceNotValidException($"The {context} is not a valid JSON object.");
            }

            if (!obj.ContainsKey("resourceType"))
            {
                _logger.LogDebug("The {Context} is missing the 'resourceType' property.", context);
                throw new ResourceNotValidException($"The {context} is missing the 'resourceType' property.");
            }
        }
        catch (ResourceNotValidException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ResourceNotValidException($"The {context} is not valid JSON: {ex.Message}");
        }
    }
}
