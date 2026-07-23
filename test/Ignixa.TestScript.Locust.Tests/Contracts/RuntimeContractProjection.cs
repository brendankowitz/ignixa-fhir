using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Reporting;

namespace Ignixa.TestScript.Locust.Tests.Contracts;

/// <summary>
/// Pure, representation-only projections shared by the runtime contract test. Every method here maps a
/// real engine artifact (a <see cref="TestResponse"/>, a <see cref="TestRequest"/>, or a
/// <see cref="TestScriptOutcome"/>) into the exact normalized JSON shape the committed contract stores, so
/// the .NET and Python halves compare identical values. Nothing here decides semantics: it only removes
/// cross-language representation noise (header casing, JSON whitespace/key-order, outcome spelling).
/// </summary>
public static class RuntimeContractProjection
{
    /// <summary>Builds the deterministic <see cref="TestResponse"/> a queued contract response describes.</summary>
    public static TestResponse BuildResponse(JsonElement spec)
    {
        int status = spec.GetProperty("status").GetInt32();

        ImmutableDictionary<string, string>.Builder headers =
            ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        if (spec.TryGetProperty("headers", out JsonElement headerElement))
        {
            foreach (JsonProperty header in headerElement.EnumerateObject())
            {
                headers[header.Name] = header.Value.GetString() ?? string.Empty;
            }
        }

        ResourceJsonNode? body = null;
        string? rawBody = null;
        string? bodyParseError = null;

        if (spec.TryGetProperty("body", out JsonElement bodyElement) && bodyElement.ValueKind != JsonValueKind.Null)
        {
            rawBody = JsonSerializer.Serialize(bodyElement);
            body = JsonSourceNodeFactory.Parse(rawBody);
        }
        else if (spec.TryGetProperty("rawBody", out JsonElement rawElement) && rawElement.ValueKind == JsonValueKind.String)
        {
            rawBody = rawElement.GetString();
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                try { body = JsonSourceNodeFactory.Parse(rawBody); }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException) { bodyParseError = ex.Message; }
            }
        }

        return new TestResponse
        {
            StatusCode = status,
            Body = body,
            RawBody = rawBody,
            BodyParseError = bodyParseError,
            Headers = headers.ToImmutable(),
        };
    }

    /// <summary>Normalizes an outbound request to <c>{ method, url, body }</c> for cross-language comparison.</summary>
    public static JsonObject NormalizeRequest(TestRequest request)
    {
        return new JsonObject
        {
            ["method"] = request.Method.Method,
            ["url"] = request.Url,
            ["body"] = NormalizeBody(request),
        };
    }

    /// <summary>The request body reduced to <c>{ "json": &lt;object&gt; }</c>, <c>{ "form": "text" }</c>, or JSON null.</summary>
    public static JsonNode? NormalizeBody(TestRequest request)
    {
        if (request.FormBody is not null)
        {
            return new JsonObject { ["form"] = request.FormBody };
        }

        if (request.Body is not null)
        {
            return new JsonObject { ["json"] = JsonNode.Parse(request.Body.SerializeToString()) };
        }

        return null;
    }

    /// <summary>The request headers as a case-insensitive lower-cased map, for containment comparison.</summary>
    public static Dictionary<string, string> LowerHeaders(TestRequest request)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> header in request.Headers)
        {
            // Header names are normalized to a canonical lower-case key so the .NET and Python
            // contract halves compare an identical, casing-independent representation.
#pragma warning disable CA1308 // Normalizing to lower case is the reviewed canonical contract form, not display.
            result[header.Key.ToLowerInvariant()] = header.Value;
#pragma warning restore CA1308
        }

        return result;
    }

    /// <summary>The camel-cased contract spelling of a <see cref="TestScriptOutcome"/>.</summary>
    public static string OutcomeToken(TestScriptOutcome outcome) => outcome switch
    {
        TestScriptOutcome.Pass => "pass",
        TestScriptOutcome.Warning => "warning",
        TestScriptOutcome.Fail => "fail",
        TestScriptOutcome.Error => "error",
        TestScriptOutcome.Skip => "skip",
        _ => throw new InvalidOperationException($"Unknown outcome '{outcome}'."),
    };

    /// <summary>The contract spelling of a report action kind.</summary>
    public static string ActionKindToken(TestActionKind kind) => kind switch
    {
        TestActionKind.Operation => "operation",
        TestActionKind.Assertion => "assertion",
        _ => throw new InvalidOperationException($"Unknown action kind '{kind}'."),
    };

    /// <summary>Structural JSON equality: objects order-insensitive, arrays order-sensitive, numbers by value.</summary>
    public static bool JsonEquivalent(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        switch (left)
        {
            case JsonObject leftObject when right is JsonObject rightObject:
                if (leftObject.Count != rightObject.Count)
                {
                    return false;
                }

                foreach (KeyValuePair<string, JsonNode?> property in leftObject)
                {
                    if (!rightObject.TryGetPropertyValue(property.Key, out JsonNode? rightValue) ||
                        !JsonEquivalent(property.Value, rightValue))
                    {
                        return false;
                    }
                }

                return true;

            case JsonArray leftArray when right is JsonArray rightArray:
                if (leftArray.Count != rightArray.Count)
                {
                    return false;
                }

                for (int i = 0; i < leftArray.Count; i++)
                {
                    if (!JsonEquivalent(leftArray[i], rightArray[i]))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValue leftValue when right is JsonValue rightValue:
                return JsonValueEquivalent(leftValue, rightValue);

            default:
                return false;
        }
    }

    private static bool JsonValueEquivalent(JsonValue left, JsonValue right)
    {
        JsonElement leftElement = ToElement(left);
        JsonElement rightElement = ToElement(right);

        if (leftElement.ValueKind != rightElement.ValueKind)
        {
            // Allow numeric/string cross-representation only when both are numbers written differently.
            if (leftElement.ValueKind == JsonValueKind.Number && rightElement.ValueKind == JsonValueKind.Number)
            {
                return leftElement.GetDecimal() == rightElement.GetDecimal();
            }

            return false;
        }

        return leftElement.ValueKind switch
        {
            JsonValueKind.Number => leftElement.GetDecimal() == rightElement.GetDecimal(),
            JsonValueKind.String => leftElement.GetString() == rightElement.GetString(),
            JsonValueKind.True or JsonValueKind.False => leftElement.GetBoolean() == rightElement.GetBoolean(),
            JsonValueKind.Null => true,
            _ => leftElement.GetRawText() == rightElement.GetRawText(),
        };
    }

    /// <summary>
    /// Materializes a <see cref="JsonValue"/> as a <see cref="JsonElement"/> whether it is backed by a
    /// parsed element (contract file) or constructed in-memory from a CLR primitive (engine projection).
    /// </summary>
    private static JsonElement ToElement(JsonValue value)
    {
        if (value.TryGetValue(out JsonElement element))
        {
            return element;
        }

        using JsonDocument document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.Clone();
    }
}
