using System.Text.Json.Nodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Evaluation;

internal static class VariableExtractor
{
    internal static TestScriptContext ExtractFromResponse(
        IReadOnlyList<VariableDefinition> variables,
        TestScriptContext context)
    {
        foreach (var variable in variables)
        {
            if (variable.Extraction is null) continue;

            var response = variable.SourceId is not null
                ? context.ResponseHistory.GetValueOrDefault(variable.SourceId)
                : context.LastResponse;

            if (response is null) continue;

            var value = ExtractValue(variable.Extraction, response);
            if (value is not null)
                context = context.WithVariable(variable.Name, value);
        }
        return context;
    }

    private static string? ExtractValue(VariableExtraction extraction, FhirResponse response) =>
        extraction switch
        {
            HeaderExtraction h => response.Headers.GetValueOrDefault(h.Field),
            PathExtraction p => ExtractFromBody(response.Body, p.Path),
            ExpressionExtraction e => ExtractFromBody(response.Body, e.Expression),
            _ => null
        };

    private static string? ExtractFromBody(JsonNode? body, string path)
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
        return current?.GetValue<string>();
    }
}
