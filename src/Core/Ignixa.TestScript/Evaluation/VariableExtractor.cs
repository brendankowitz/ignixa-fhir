using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;

namespace Ignixa.TestScript.Evaluation;

internal static class VariableExtractor
{
    internal static TestScriptContext ExtractFromResponse(
        IReadOnlyList<VariableDefinition> variables,
        TestScriptContext context,
        IFhirSchemaProvider schema)
    {
        foreach (var variable in variables)
        {
            if (variable.Extraction is null) continue;

            var response = variable.SourceId is not null
                ? context.ResponseHistory.GetValueOrDefault(variable.SourceId)
                : context.LastResponse;

            if (response is null) continue;

            try
            {
                var value = ExtractValue(variable.Extraction, response, schema);
                if (value is not null)
                    context = context.WithVariable(variable.Name, value);
            }
            catch (VariableExtractionException ex)
            {
                context.Recorder.RecordOperationResult(
                    $"variable:{variable.Name}",
                    $"Extract variable '{variable.Name}'",
                    new OperationOutcome(false, ErrorMessage: ex.Message));
            }
        }
        return context;
    }

    private static string? ExtractValue(VariableExtraction extraction, TestResponse response, IFhirSchemaProvider schema) =>
        extraction switch
        {
            HeaderExtraction h => response.Headers.GetValueOrDefault(h.Field),
            PathExtraction p => ExtractFromBodyByPath(response.Body?.MutableNode, p.Path),
            ExpressionExtraction e => ExtractFromBodyByExpression(response.Body, schema, e.Expression),
            _ => throw new InvalidOperationException(
                $"Unhandled variable extraction type: {extraction.GetType().Name}")
        };

    private static string? ExtractFromBodyByPath(JsonNode? body, string path)
    {
        if (body is null) return null;

        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = body;
        foreach (var part in parts)
        {
            if (current is JsonObject obj)
                current = obj[part];
            else
                return null;
        }

        return current switch
        {
            null => null,
            JsonValue value => value.TryGetValue<string>(out var s) ? s : value.ToJsonString(),
            _ => current.ToJsonString()
        };
    }

    private static string? ExtractFromBodyByExpression(ResourceJsonNode? body, IFhirSchemaProvider schema, string expression)
    {
        if (body is null) return null;
        try
        {
            return body.ToElement(schema).Scalar(expression)?.ToString();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new VariableExtractionException(
                $"FHIRPath extraction expression '{expression}' failed: {ex.Message}", ex);
        }
    }
}
