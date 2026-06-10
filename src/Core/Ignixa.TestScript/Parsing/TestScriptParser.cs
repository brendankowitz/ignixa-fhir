using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Parsing;

public static class TestScriptParser
{
    public static ParseResult<TestScriptDefinition> Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return ParseResult<TestScriptDefinition>.Failure(
                new ParseError(ParseSeverity.Error, $"Invalid JSON: {ex.Message}"));
        }

        if (root is not JsonObject obj)
            return ParseResult<TestScriptDefinition>.Failure(
                new ParseError(ParseSeverity.Error, "Expected JSON object"));

        var errors = new List<ParseError>();

        var name = JsonFieldReader.GetString(obj, "name", "$", errors);
        if (string.IsNullOrEmpty(name) && !errors.Any(e => e.Path == "$.name"))
            errors.Add(new ParseError(ParseSeverity.Error, "Required field 'name' is missing", "$.name"));

        if (errors.Any(e => e.Severity == ParseSeverity.Error))
            return ParseResult<TestScriptDefinition>.Failure([.. errors]);

        var status = JsonFieldReader.GetString(obj, "status", "$", errors);
        if (string.IsNullOrEmpty(status) && !errors.Any(e => e.Path == "$.status"))
            errors.Add(new ParseError(ParseSeverity.Warning, "Recommended field 'status' is missing", "$.status"));

        var metadata = new TestScriptMetadata
        {
            Name = name!,
            Description = JsonFieldReader.GetString(obj, "description", "$", errors),
            Url = JsonFieldReader.GetString(obj, "url", "$", errors),
            Status = status,
            Version = JsonFieldReader.GetString(obj, "version", "$", errors)
        };

        var fixtures = ParseFixtures(obj["fixture"]?.AsArray(), errors);
        var variables = ParseVariables(obj["variable"]?.AsArray(), errors);
        var profiles = ParseProfiles(obj["profile"]?.AsArray(), errors);
        var setup = ParseSetupActions(obj["setup"]?["action"]?.AsArray(), errors);
        var tests = ParseTests(obj["test"]?.AsArray(), errors);
        var teardown = ParseTeardownActions(obj["teardown"]?["action"]?.AsArray(), errors);

        var definition = new TestScriptDefinition
        {
            Metadata = metadata,
            Profiles = profiles,
            Fixtures = fixtures,
            Variables = variables,
            Setup = setup,
            Tests = tests,
            Teardown = teardown
        };

        if (errors.Any(e => e.Severity == ParseSeverity.Error))
            return ParseResult<TestScriptDefinition>.Failure([.. errors]);

        return errors.Count > 0
            ? ParseResult<TestScriptDefinition>.WithWarnings(definition, errors)
            : ParseResult<TestScriptDefinition>.Success(definition);
    }

    public static ParseResult<TestScriptDefinition> ParseFile(string filePath)
    {
        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ParseResult<TestScriptDefinition>.Failure(
                new ParseError(ParseSeverity.Error, $"Cannot read file '{filePath}': {ex.Message}"));
        }
        return Parse(json);
    }

    private static IReadOnlyList<FixtureDefinition> ParseFixtures(JsonArray? fixtures, List<ParseError> errors)
    {
        if (fixtures is null) return [];
        var result = new List<FixtureDefinition>();
        for (var i = 0; i < fixtures.Count; i++)
        {
            var path = $"fixture[{i}]";
            if (fixtures[i] is not JsonObject fix)
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Fixture entry is not an object", path));
                continue;
            }

            var id = JsonFieldReader.GetString(fix, "id", path, errors);
            if (string.IsNullOrEmpty(id))
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Fixture is missing required field 'id'", $"{path}.id"));
                continue;
            }

            ResourceJsonNode? fixtureResource = null;
            if (fix["resource"] is JsonNode resourceNode)
            {
                try
                {
                    fixtureResource = JsonSourceNodeFactory.Parse(resourceNode);
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
                {
                    errors.Add(new ParseError(ParseSeverity.Error,
                        $"Fixture resource could not be parsed: {ex.Message}", $"{path}.resource"));
                    continue;
                }
            }

            result.Add(new FixtureDefinition
            {
                Id = id,
                Resource = fixtureResource,
                Autocreate = JsonFieldReader.GetBool(fix, "autocreate", path, errors) ?? false,
                Autodelete = JsonFieldReader.GetBool(fix, "autodelete", path, errors) ?? false
            });
        }
        return result;
    }

    private static IReadOnlyList<VariableDefinition> ParseVariables(JsonArray? variables, List<ParseError> errors)
    {
        if (variables is null) return [];
        var result = new List<VariableDefinition>();
        for (var i = 0; i < variables.Count; i++)
        {
            var path = $"variable[{i}]";
            if (variables[i] is not JsonObject v)
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Variable entry is not an object", path));
                continue;
            }

            var name = JsonFieldReader.GetString(v, "name", path, errors);
            if (string.IsNullOrEmpty(name))
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Variable is missing required field 'name'", $"{path}.name"));
                continue;
            }

            result.Add(new VariableDefinition
            {
                Name = name,
                DefaultValue = JsonFieldReader.GetString(v, "defaultValue", path, errors),
                SourceId = JsonFieldReader.GetString(v, "sourceId", path, errors),
                Description = JsonFieldReader.GetString(v, "description", path, errors),
                Extraction = BuildVariableExtraction(v, path, errors)
            });
        }
        return result;
    }

    private static VariableExtraction? BuildVariableExtraction(JsonObject v, string path, List<ParseError> errors)
    {
        if (JsonFieldReader.GetString(v, "expression", path, errors) is { } expr)
            return new ExpressionExtraction(expr);
        if (JsonFieldReader.GetString(v, "path", path, errors) is { } pathValue)
            return new PathExtraction(pathValue);
        if (JsonFieldReader.GetString(v, "headerField", path, errors) is { } field)
            return new HeaderExtraction(field);
        return null;
    }

    private static IReadOnlyList<ProfileReference> ParseProfiles(JsonArray? profiles, List<ParseError> errors)
    {
        if (profiles is null) return [];
        var result = new List<ProfileReference>();
        for (var i = 0; i < profiles.Count; i++)
        {
            var path = $"profile[{i}]";
            if (profiles[i] is not JsonObject p)
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Profile entry is not an object", path));
                continue;
            }

            var id = JsonFieldReader.GetString(p, "id", path, errors);
            if (string.IsNullOrEmpty(id))
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Profile is missing required field 'id'", $"{path}.id"));
                continue;
            }

            var reference = JsonFieldReader.GetString(p, "reference", path, errors) ?? string.Empty;
            result.Add(new ProfileReference { Id = id, Canonical = reference });
        }
        return result;
    }

    private static IReadOnlyList<TestPhaseDefinition> ParseTests(JsonArray? tests, List<ParseError> errors)
    {
        if (tests is null) return [];
        var result = new List<TestPhaseDefinition>();
        for (var i = 0; i < tests.Count; i++)
        {
            var path = $"test[{i}]";
            if (tests[i] is not JsonObject test)
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Test entry is not an object", path));
                continue;
            }

            var extensions = test["extension"]?.AsArray();
            var name = JsonFieldReader.GetString(test, "name", path, errors) ?? "Unnamed";
            result.Add(new TestPhaseDefinition
            {
                Name = name,
                Description = JsonFieldReader.GetString(test, "description", path, errors),
                Actions = ParseActions(test["action"]?.AsArray(), path, errors),
                Parameters = ParseParametrize(extensions, name, errors),
                FhirVersions = ParseFhirVersions(extensions)
            });
        }
        return result;
    }

    private const string ParametrizeUrl = "http://ignixa.io/testscript/parametrize";
    private const string FhirVersionsUrl = "http://ignixa.io/testscript/fhirVersions";

    private static IReadOnlyList<string> ParseFhirVersions(JsonArray? extensions)
    {
        if (extensions is null) return [];
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (obj["url"]?.GetValue<string>() != FhirVersionsUrl) continue;

            if (obj["valueString"]?.GetValue<string>() is { } versions)
                return versions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return [];
    }

    private static ParametrizeDefinition? ParseParametrize(JsonArray? extensions, string testName, List<ParseError> errors)
    {
        if (extensions is null) return null;
        var found = new List<ParametrizeDefinition>();
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (obj["url"]?.GetValue<string>() != ParametrizeUrl) continue;

            var nested = obj["extension"]?.AsArray();
            if (nested is null) continue;

            string? variable = null;
            string? values = null;
            foreach (var n in nested)
            {
                if (n is not JsonObject nObj) continue;
                var url = nObj["url"]?.GetValue<string>();
                if (url == "variable") variable = nObj["valueString"]?.GetValue<string>();
                else if (url == "values") values = nObj["valueString"]?.GetValue<string>();
            }

            if (variable is not null && values is not null)
                found.Add(new ParametrizeDefinition(
                    variable,
                    values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }

        if (found.Count > 1)
            errors.Add(new ParseError(ParseSeverity.Warning,
                $"Test '{testName}' has {found.Count} parametrize extensions; only the first will be used."));

        return found.Count > 0 ? found[0] : null;
    }

    private static readonly HashSet<string> KnownHttpMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };

    private static IReadOnlyList<ActionExpression> ParseActions(JsonArray? actions, string testPath, List<ParseError> errors)
    {
        if (actions is null) return [];
        var result = new List<ActionExpression>();
        for (var i = 0; i < actions.Count; i++)
        {
            if (ParseAction(actions[i], $"{testPath}.action[{i}]", errors) is { } action)
                result.Add(action);
        }
        return result;
    }

    private static IReadOnlyList<ActionExpression> ParseSetupActions(JsonArray? actions, List<ParseError> errors)
    {
        if (actions is null) return [];
        var result = new List<ActionExpression>();
        for (var i = 0; i < actions.Count; i++)
        {
            if (ParseAction(actions[i], $"setup.action[{i}]", errors) is { } action)
                result.Add(action);
        }
        return result;
    }

    private static IReadOnlyList<OperationExpression> ParseTeardownActions(JsonArray? actions, List<ParseError> errors)
    {
        if (actions is null) return [];
        var result = new List<OperationExpression>();
        for (var i = 0; i < actions.Count; i++)
        {
            var path = $"teardown.action[{i}]";
            if (actions[i] is not JsonObject action)
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Teardown action is not an object", path));
                continue;
            }

            if (action["operation"] is JsonObject op)
            {
                result.Add(ParseOperation(op, $"{path}.operation", errors));
            }
            else if (action["assert"] is JsonObject)
            {
                errors.Add(new ParseError(ParseSeverity.Error,
                    "Teardown actions may only contain operations, not asserts (per FHIR TestScript spec)", path));
            }
            else
            {
                errors.Add(new ParseError(ParseSeverity.Error,
                    "Teardown action has neither 'operation' nor a valid key", path));
            }
        }
        return result;
    }

    private static ActionExpression? ParseAction(JsonNode? item, string path, List<ParseError> errors)
    {
        if (item is not JsonObject action)
        {
            errors.Add(new ParseError(ParseSeverity.Error, "Action is not an object", path));
            return null;
        }

        if (action["operation"] is JsonObject op)
            return ParseOperation(op, $"{path}.operation", errors);

        if (action["assert"] is JsonObject assert)
            return ParseAssert(assert, $"{path}.assert", errors);

        errors.Add(new ParseError(ParseSeverity.Error,
            $"Action has neither 'operation' nor 'assert' key (found keys: {DescribeKeys(action)})", path));
        return null;
    }

    private static string DescribeKeys(JsonObject obj)
    {
        var keys = obj.Select(kvp => kvp.Key).ToList();
        return keys.Count == 0 ? "none" : string.Join(", ", keys);
    }

    private static OperationExpression ParseOperation(JsonObject op, string path, List<ParseError> errors)
    {
        var typeCode = op["type"]?["code"]?.GetValue<string>() ?? "read";
        var methodStr = JsonFieldReader.GetString(op, "method", path, errors);

        if (methodStr is not null && !KnownHttpMethods.Contains(methodStr))
            errors.Add(new ParseError(ParseSeverity.Warning,
                $"Unknown HTTP method '{methodStr}'; expected one of GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS.", $"{path}.method"));

        return new OperationExpression
        {
            Type = typeCode,
            Resource = JsonFieldReader.GetString(op, "resource", path, errors),
            Url = JsonFieldReader.GetString(op, "url", path, errors),
            Params = JsonFieldReader.GetString(op, "params", path, errors),
            Method = methodStr is not null ? new HttpMethod(methodStr) : null,
            Accept = JsonFieldReader.GetString(op, "accept", path, errors),
            ContentType = JsonFieldReader.GetString(op, "contentType", path, errors),
            SourceId = JsonFieldReader.GetString(op, "sourceId", path, errors),
            TargetId = JsonFieldReader.GetString(op, "targetId", path, errors),
            ResponseId = JsonFieldReader.GetString(op, "responseId", path, errors),
            RequestId = JsonFieldReader.GetString(op, "requestId", path, errors),
            Label = JsonFieldReader.GetString(op, "label", path, errors),
            Description = JsonFieldReader.GetString(op, "description", path, errors),
            Destination = op["destination"] is JsonValue dv && dv.TryGetValue<int>(out var dest) ? dest : null,
            Origin = op["origin"] is JsonValue ov && ov.TryGetValue<int>(out var orig) ? orig : null,
            EncodeRequestUrl = JsonFieldReader.GetBool(op, "encodeRequestUrl", path, errors) ?? true,
            Headers = ParseHeaders(op["requestHeader"]?.AsArray(), path, errors)
        };
    }

    private static AssertExpression ParseAssert(JsonObject a, string path, List<ParseError> errors)
    {
        var operatorVal = ParseOperator(JsonFieldReader.GetString(a, "operator", path, errors), path, errors);
        var criteria = BuildAssertCriteria(a, operatorVal, path, errors);

        return new AssertExpression
        {
            Criteria = criteria,
            SourceId = JsonFieldReader.GetString(a, "sourceId", path, errors),
            WarningOnly = JsonFieldReader.GetBool(a, "warningOnly", path, errors) ?? false,
            Label = JsonFieldReader.GetString(a, "label", path, errors),
            Description = JsonFieldReader.GetString(a, "description", path, errors),
            Direction = ParseDirection(JsonFieldReader.GetString(a, "direction", path, errors))
        };
    }

    private static readonly string[] KnownCriteriaFields =
    [
        "response", "responseCode", "contentType", "resource",
        "headerField", "expression", "requestMethod", "requestURL"
    ];

    private static AssertCriteria BuildAssertCriteria(JsonObject a, AssertOperator? op, string path, List<ParseError> errors)
    {
        if (JsonFieldReader.GetString(a, "response", path, errors) is { } response)
            return new ResponseStatusCriteria(response);
        if (JsonFieldReader.GetString(a, "responseCode", path, errors) is { } code)
            return new ResponseCodeCriteria(code);
        if (JsonFieldReader.GetString(a, "contentType", path, errors) is { } ct)
            return new ContentTypeCriteria(ct);
        if (JsonFieldReader.GetString(a, "resource", path, errors) is { } resource)
            return new ResourceTypeCriteria(resource);
        if (JsonFieldReader.GetString(a, "headerField", path, errors) is { } field)
            return new HeaderCriteria(field, JsonFieldReader.GetString(a, "value", path, errors), op);
        if (JsonFieldReader.GetString(a, "expression", path, errors) is { } expr)
        {
            var value = JsonFieldReader.GetString(a, "value", path, errors);
            return value is not null
                ? new FhirPathValueCriteria(expr, value, op ?? AssertOperator.Equals)
                : new FhirPathCriteria(expr);
        }
        if (JsonFieldReader.GetString(a, "requestMethod", path, errors) is { } method)
            return new RequestMethodCriteria(method);
        if (JsonFieldReader.GetString(a, "requestURL", path, errors) is { } url)
            return new RequestUrlCriteria(url, op);

        var presentFields = a
            .Where(kvp => !IsAssertMetadataField(kvp.Key))
            .Select(kvp => kvp.Key)
            .ToList();
        var fieldList = presentFields.Count > 0 ? string.Join(", ", presentFields) : "none";

        errors.Add(new ParseError(ParseSeverity.Error,
            $"Assert action has no supported criteria field (one of {string.Join(", ", KnownCriteriaFields)}); " +
            $"unsupported field(s) present: {fieldList}", path));

        return new ResponseCodeCriteria("200");
    }

    private static bool IsAssertMetadataField(string key) => key is
        "operator" or "sourceId" or "warningOnly" or "label" or "description" or "direction" or "value";

    private static IReadOnlyList<HeaderExpression> ParseHeaders(JsonArray? headers, string opPath, List<ParseError> errors)
    {
        if (headers is null) return [];
        var result = new List<HeaderExpression>();
        for (var i = 0; i < headers.Count; i++)
        {
            var path = $"{opPath}.requestHeader[{i}]";
            if (headers[i] is not JsonObject h)
            {
                errors.Add(new ParseError(ParseSeverity.Error, "Request header entry is not an object", path));
                continue;
            }

            var field = JsonFieldReader.GetString(h, "field", path, errors);
            var value = JsonFieldReader.GetString(h, "value", path, errors);
            if (field is not null && value is not null)
                result.Add(new HeaderExpression { Field = field, Value = value });
            else
                errors.Add(new ParseError(ParseSeverity.Error,
                    "Request header must have both 'field' and 'value'", path));
        }
        return result;
    }

    private static readonly HashSet<string> KnownAssertOperators =
        new(StringComparer.Ordinal)
        {
            "equals", "notEquals", "in", "notIn", "greaterThan", "lessThan",
            "empty", "notEmpty", "contains", "notContains", "eval"
        };

    private static AssertOperator? ParseOperator(string? op, string path, List<ParseError> errors)
    {
        switch (op)
        {
            case null:
                return null;
            case "equals":
                return AssertOperator.Equals;
            case "notEquals":
                return AssertOperator.NotEquals;
            case "in":
                return AssertOperator.In;
            case "notIn":
                return AssertOperator.NotIn;
            case "contains":
                return AssertOperator.Contains;
            case "notContains":
                return AssertOperator.NotContains;
            case "greaterThan":
                return AssertOperator.GreaterThan;
            case "lessThan":
                return AssertOperator.LessThan;
            case "empty":
                return AssertOperator.Empty;
            case "notEmpty":
                return AssertOperator.NotEmpty;
            case "eval":
                errors.Add(new ParseError(ParseSeverity.Error,
                    "Assert operator 'eval' is not supported", $"{path}.operator"));
                return null;
            default:
                var suffix = KnownAssertOperators.Contains(op)
                    ? string.Empty
                    : $" (expected one of {string.Join(", ", KnownAssertOperators)})";
                errors.Add(new ParseError(ParseSeverity.Error,
                    $"Unknown assert operator '{op}'{suffix}", $"{path}.operator"));
                return null;
        }
    }

    private static AssertDirection ParseDirection(string? dir) => dir switch
    {
        "request" => AssertDirection.Request,
        _ => AssertDirection.Response
    };
}
