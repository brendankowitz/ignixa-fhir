using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Model;

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

            var value = ExtractValue(variable.Extraction, response, schema);
            if (value is not null)
                context = context.WithVariable(variable.Name, value);
        }
        return context;
    }

    private static string? ExtractValue(VariableExtraction extraction, TestResponse response, IFhirSchemaProvider schema) =>
        extraction switch
        {
            HeaderExtraction h => response.Headers.GetValueOrDefault(h.Field),
            PathExtraction p => ExtractFromBody(response.Body, schema, p.Path),
            ExpressionExtraction e => ExtractFromBody(response.Body, schema, e.Expression),
            _ => null
        };

    private static string? ExtractFromBody(Ignixa.Serialization.SourceNodes.ResourceJsonNode? body, IFhirSchemaProvider schema, string expression)
    {
        if (body is null) return null;
        return body.ToElement(schema).Scalar(expression)?.ToString();
    }
}
