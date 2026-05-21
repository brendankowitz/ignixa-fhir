using System.Text.Json;
using System.Text.Json.Nodes;
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

        var metadata = new TestScriptMetadata
        {
            Name = name!,
            Description = obj["description"]?.GetValue<string>(),
            Url = obj["url"]?.GetValue<string>(),
            Status = obj["status"]?.GetValue<string>(),
            Version = obj["version"]?.GetValue<string>()
        };

        var fixtures = ParseFixtures(obj["fixture"]?.AsArray());
        var variables = ParseVariables(obj["variable"]?.AsArray());
        var profiles = ParseProfiles(obj["profile"]?.AsArray());
        var setup = ParseActions(obj["setup"]?["action"]?.AsArray());
        var tests = ParseTests(obj["test"]?.AsArray());
        var teardown = ParseActions(obj["teardown"]?["action"]?.AsArray());

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
        var json = File.ReadAllText(filePath);
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
                Resource = fix["resource"],
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
                Expression = v["expression"]?.GetValue<string>(),
                Path = v["path"]?.GetValue<string>(),
                HeaderField = v["headerField"]?.GetValue<string>(),
                SourceId = v["sourceId"]?.GetValue<string>(),
                Description = v["description"]?.GetValue<string>()
            });
        }
        return result;
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

    private static IReadOnlyList<TestPhaseDefinition> ParseTests(JsonArray? tests)
    {
        if (tests is null) return [];
        var result = new List<TestPhaseDefinition>();
        foreach (var item in tests)
        {
            if (item is not JsonObject test) continue;
            result.Add(new TestPhaseDefinition
            {
                Name = test["name"]?.GetValue<string>() ?? "Unnamed",
                Description = test["description"]?.GetValue<string>(),
                Actions = ParseActions(test["action"]?.AsArray())
            });
        }
        return result;
    }

    private static IReadOnlyList<ActionExpression> ParseActions(JsonArray? actions)
    {
        if (actions is null) return [];
        var result = new List<ActionExpression>();
        foreach (var item in actions)
        {
            if (item is not JsonObject action) continue;

            if (action["operation"] is JsonObject op)
                result.Add(ParseOperation(op));
            else if (action["assert"] is JsonObject assert)
                result.Add(ParseAssert(assert));
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

    private static AssertExpression ParseAssert(JsonObject a)
    {
        var operatorStr = a["operator"]?.GetValue<string>();

        return new AssertExpression
        {
            Response = a["response"]?.GetValue<string>(),
            ResponseCode = a["responseCode"]?.GetValue<string>(),
            ContentType = a["contentType"]?.GetValue<string>(),
            Expression = a["expression"]?.GetValue<string>(),
            Path = a["path"]?.GetValue<string>(),
            Value = a["value"]?.GetValue<string>(),
            SourceId = a["sourceId"]?.GetValue<string>(),
            CompareToSourceId = a["compareToSourceId"]?.GetValue<string>(),
            CompareToSourceExpression = a["compareToSourceExpression"]?.GetValue<string>(),
            CompareToSourcePath = a["compareToSourcePath"]?.GetValue<string>(),
            ValidateProfileId = a["validateProfileId"]?.GetValue<string>(),
            Resource = a["resource"]?.GetValue<string>(),
            MinimumId = a["minimumId"]?.GetValue<string>(),
            HeaderField = a["headerField"]?.GetValue<string>(),
            RequestMethod = a["requestMethod"]?.GetValue<string>(),
            RequestUrl = a["requestURL"]?.GetValue<string>(),
            NavigationLinks = a["navigationLinks"]?.GetValue<bool>(),
            Operator = ParseOperator(operatorStr),
            WarningOnly = a["warningOnly"]?.GetValue<bool>() ?? false,
            Label = a["label"]?.GetValue<string>(),
            Description = a["description"]?.GetValue<string>(),
            Direction = ParseDirection(a["direction"]?.GetValue<string>())
        };
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
