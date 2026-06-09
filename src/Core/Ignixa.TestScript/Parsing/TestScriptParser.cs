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

        var name = obj["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
            errors.Add(new ParseError(ParseSeverity.Error, "Required field 'name' is missing"));

        if (errors.Any(e => e.Severity == ParseSeverity.Error))
            return ParseResult<TestScriptDefinition>.Failure([.. errors]);

        var status = obj["status"]?.GetValue<string>();
        if (string.IsNullOrEmpty(status))
            errors.Add(new ParseError(ParseSeverity.Warning, "Recommended field 'status' is missing"));

        var metadata = new TestScriptMetadata
        {
            Name = name!,
            Description = obj["description"]?.GetValue<string>(),
            Url = obj["url"]?.GetValue<string>(),
            Status = status,
            Version = obj["version"]?.GetValue<string>()
        };

        var fixtures = ParseFixtures(obj["fixture"]?.AsArray());
        var variables = ParseVariables(obj["variable"]?.AsArray());
        var profiles = ParseProfiles(obj["profile"]?.AsArray());
        var setup = ParseOperationActions(obj["setup"]?["action"]?.AsArray());
        var tests = ParseTests(obj["test"]?.AsArray(), errors);
        var teardown = ParseOperationActions(obj["teardown"]?["action"]?.AsArray());

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

    private static IReadOnlyList<FixtureDefinition> ParseFixtures(JsonArray? fixtures)
    {
        if (fixtures is null) return [];
        var result = new List<FixtureDefinition>();
        foreach (var item in fixtures)
        {
            if (item is not JsonObject fix) continue;
            result.Add(new FixtureDefinition
            {
                Id = fix["id"]?.GetValue<string>() ?? string.Empty,
                Resource = fix["resource"] is JsonNode resourceNode ? JsonSourceNodeFactory.Parse(resourceNode) : null,
                Autocreate = fix["autocreate"]?.GetValue<bool>() ?? false,
                Autodelete = fix["autodelete"]?.GetValue<bool>() ?? false
            });
        }
        return result;
    }

    private static IReadOnlyList<VariableDefinition> ParseVariables(JsonArray? variables)
    {
        if (variables is null) return [];
        var result = new List<VariableDefinition>();
        foreach (var item in variables)
        {
            if (item is not JsonObject v) continue;
            result.Add(new VariableDefinition
            {
                Name = v["name"]?.GetValue<string>() ?? string.Empty,
                DefaultValue = v["defaultValue"]?.GetValue<string>(),
                SourceId = v["sourceId"]?.GetValue<string>(),
                Description = v["description"]?.GetValue<string>(),
                Extraction = BuildVariableExtraction(v)
            });
        }
        return result;
    }

    private static VariableExtraction? BuildVariableExtraction(JsonObject v)
    {
        if (v["expression"]?.GetValue<string>() is { } expr)
            return new ExpressionExtraction(expr);
        if (v["path"]?.GetValue<string>() is { } path)
            return new PathExtraction(path);
        if (v["headerField"]?.GetValue<string>() is { } field)
            return new HeaderExtraction(field);
        return null;
    }

    private static IReadOnlyList<ProfileReference> ParseProfiles(JsonArray? profiles)
    {
        if (profiles is null) return [];
        var result = new List<ProfileReference>();
        foreach (var item in profiles)
        {
            if (item is not JsonObject p) continue;
            var id = p["id"]?.GetValue<string>() ?? string.Empty;
            var reference = p["reference"]?.GetValue<string>() ?? string.Empty;
            result.Add(new ProfileReference { Id = id, Canonical = reference });
        }
        return result;
    }

    private static IReadOnlyList<TestPhaseDefinition> ParseTests(JsonArray? tests, List<ParseError> errors)
    {
        if (tests is null) return [];
        var result = new List<TestPhaseDefinition>();
        foreach (var item in tests)
        {
            if (item is not JsonObject test) continue;
            var extensions = test["extension"]?.AsArray();
            var name = test["name"]?.GetValue<string>() ?? "Unnamed";
            result.Add(new TestPhaseDefinition
            {
                Name = name,
                Description = test["description"]?.GetValue<string>(),
                Actions = ParseActions(test["action"]?.AsArray(), errors),
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

    private static IReadOnlyList<ActionExpression> ParseActions(JsonArray? actions, List<ParseError> errors)
    {
        if (actions is null) return [];
        var result = new List<ActionExpression>();
        foreach (var item in actions)
        {
            if (item is not JsonObject action) continue;

            if (action["operation"] is JsonObject op)
                result.Add(ParseOperation(op));
            else if (action["assert"] is JsonObject assert)
                result.Add(ParseAssert(assert, errors));
        }
        return result;
    }

    private static IReadOnlyList<OperationExpression> ParseOperationActions(JsonArray? actions)
    {
        if (actions is null) return [];
        var result = new List<OperationExpression>();
        foreach (var item in actions)
        {
            if (item is not JsonObject action) continue;
            if (action["operation"] is JsonObject op)
                result.Add(ParseOperation(op));
        }
        return result;
    }

    private static OperationExpression ParseOperation(JsonObject op)
    {
        var typeCode = op["type"]?["code"]?.GetValue<string>() ?? "read";
        var methodStr = op["method"]?.GetValue<string>();

        return new OperationExpression
        {
            Type = typeCode,
            Resource = op["resource"]?.GetValue<string>(),
            Url = op["url"]?.GetValue<string>(),
            Params = op["params"]?.GetValue<string>(),
            Method = methodStr is not null ? new HttpMethod(methodStr) : null,
            Accept = op["accept"]?.GetValue<string>(),
            ContentType = op["contentType"]?.GetValue<string>(),
            SourceId = op["sourceId"]?.GetValue<string>(),
            TargetId = op["targetId"]?.GetValue<string>(),
            ResponseId = op["responseId"]?.GetValue<string>(),
            RequestId = op["requestId"]?.GetValue<string>(),
            Label = op["label"]?.GetValue<string>(),
            Description = op["description"]?.GetValue<string>(),
            Destination = op["destination"]?.GetValue<int>(),
            Origin = op["origin"]?.GetValue<int>(),
            EncodeRequestUrl = op["encodeRequestUrl"]?.GetValue<bool>() ?? true,
            Headers = ParseHeaders(op["requestHeader"]?.AsArray())
        };
    }

    private static AssertExpression ParseAssert(JsonObject a, List<ParseError> errors)
    {
        var operatorVal = ParseOperator(a["operator"]?.GetValue<string>());
        var criteria = BuildAssertCriteria(a, operatorVal, errors);

        return new AssertExpression
        {
            Criteria = criteria,
            SourceId = a["sourceId"]?.GetValue<string>(),
            WarningOnly = a["warningOnly"]?.GetValue<bool>() ?? false,
            Label = a["label"]?.GetValue<string>(),
            Description = a["description"]?.GetValue<string>(),
            Direction = ParseDirection(a["direction"]?.GetValue<string>())
        };
    }

    private static AssertCriteria BuildAssertCriteria(JsonObject a, AssertOperator? op, List<ParseError> errors)
    {
        if (a["response"]?.GetValue<string>() is { } response)
            return new ResponseStatusCriteria(response);
        if (a["responseCode"]?.GetValue<string>() is { } code)
            return new ResponseCodeCriteria(code);
        if (a["contentType"]?.GetValue<string>() is { } ct)
            return new ContentTypeCriteria(ct);
        if (a["resource"]?.GetValue<string>() is { } resource)
            return new ResourceTypeCriteria(resource);
        if (a["headerField"]?.GetValue<string>() is { } field)
            return new HeaderCriteria(field, a["value"]?.GetValue<string>(), op);
        if (a["expression"]?.GetValue<string>() is { } expr)
        {
            var value = a["value"]?.GetValue<string>();
            return value is not null
                ? new FhirPathValueCriteria(expr, value, op ?? AssertOperator.Equals)
                : new FhirPathCriteria(expr);
        }
        if (a["requestMethod"]?.GetValue<string>() is { } method)
            return new RequestMethodCriteria(method);
        if (a["requestURL"]?.GetValue<string>() is { } url)
            return new RequestUrlCriteria(url, op);

        errors.Add(new ParseError(ParseSeverity.Warning,
            "Assert action has no recognisable criteria field (response, responseCode, contentType, resource, headerField, expression, requestMethod, requestURL); assertion will always check for HTTP 200"));
        return new ResponseCodeCriteria("200");
    }

    private static IReadOnlyList<HeaderExpression> ParseHeaders(JsonArray? headers)
    {
        if (headers is null) return [];
        var result = new List<HeaderExpression>();
        foreach (var item in headers)
        {
            if (item is not JsonObject h) continue;
            var field = h["field"]?.GetValue<string>();
            var value = h["value"]?.GetValue<string>();
            if (field is not null && value is not null)
                result.Add(new HeaderExpression { Field = field, Value = value });
        }
        return result;
    }

    private static AssertOperator? ParseOperator(string? op) => op switch
    {
        "equals" => AssertOperator.Equals,
        "notEquals" => AssertOperator.NotEquals,
        "in" => AssertOperator.In,
        "notIn" => AssertOperator.NotIn,
        "contains" => AssertOperator.Contains,
        "notContains" => AssertOperator.NotContains,
        "greaterThan" => AssertOperator.GreaterThan,
        "lessThan" => AssertOperator.LessThan,
        "empty" => AssertOperator.Empty,
        "notEmpty" => AssertOperator.NotEmpty,
        _ => null
    };

    private static AssertDirection ParseDirection(string? dir) => dir switch
    {
        "request" => AssertDirection.Request,
        _ => AssertDirection.Response
    };
}
