/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for SqlOnFhirEvaluator.
 * Tests ViewDefinition evaluation with WHERE clauses, SELECT groups, and forEach semantics.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Models;

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNavigator used for legacy tests

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Unit tests for SqlOnFhirEvaluator.
/// Tests SQL on FHIR v2 ViewDefinition evaluation.
/// </summary>
public class SqlOnFhirEvaluatorTests
{
    private readonly SqlOnFhirEvaluator _evaluator = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly IFhirSchemaProvider _schemaProvider =
        FhirSpecificationExtensions.FromVersionString("4.0.1").GetSchemaProvider();
    private static readonly string[] _batchUnionAllExpectedIds = ["pt1", "pt2", "pt1", "pt2"];
    private static readonly string[] _batchUnionAllExpectedSources = ["a", "a", "b", "b"];
    private static readonly string[] _batchWhereExpectedIds = ["pt1", "pt1"];
    private static readonly string[] _batchWhereExpectedSources = ["a", "b"];
    private static readonly string[] _multiGivenNames = ["Alice", "Bob"];

    [Fact]
    public void GivenSimpleColumnPath_WhenEvaluated_ThenReturnsValue()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("P001", rows[0]["id"]);
    }

    [Fact]
    public void GivenMultipleColumns_WhenEvaluated_ThenReturnsRowWithAllColumns()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "active", true }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" },
                        new ViewColumnDefinition { Name = "active", Path = "active", Type = "boolean" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("P001", rows[0]["id"]);
        Assert.Equal(true, rows[0]["active"]);
    }

    [Fact]
    public void GivenMissingColumn_WhenEvaluated_ThenReturnsNull()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "birthDate", Path = "birthDate", Type = "date" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Null(rows[0]["birthDate"]);
    }

    [Fact]
    public void GivenBooleanColumn_WhenEvaluated_ThenConvertsCorrectly()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "active", true }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "is_active", Path = "active", Type = "boolean" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.IsType<bool>(rows[0]["is_active"]);
        Assert.Equal(true, rows[0]["is_active"]);
    }

    [Fact]
    public void GivenIntegerType_WhenEvaluated_ThenConvertsCorrectly()
    {
        // Arrange
        var observationJson = new Dictionary<string, object?>
        {
            { "resourceType", "Observation" },
            { "value", 42 }
        };
        var resource = CreateTypedElement(observationJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Observation",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "value", Path = "value", Type = "integer" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.IsType<int>(rows[0]["value"]);
        Assert.Equal(42, rows[0]["value"]);
    }

    [Fact]
    public void GivenWhereClause_WhenEvaluated_ThenIncludesMatchingResource()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "active", true }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Where = new List<WhereClause>
            {
                new WhereClause { Path = "active = true" }
            },
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("P001", rows[0]["id"]);
    }

    [Fact]
    public void GivenWhereClause_WhenEvaluated_ThenExcludesNonMatchingResource()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "active", false }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Where = new List<WhereClause>
            {
                new WhereClause { Path = "active = true" }
            },
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Empty(rows);
    }

    [Fact]
    public void GivenForEach_WhenEvaluated_ThenCreatesRowPerArrayElement()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "name", new object[]
                {
                    new Dictionary<string, object?> { { "family", "Smith" } },
                    new Dictionary<string, object?> { { "family", "Doe" } }
                }
            }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                },
                new SelectGroup
                {
                    ForEach = "name",
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.Equal("Smith", rows[0]["family"]);
        Assert.Equal("Doe", rows[1]["family"]);
    }

    [Fact]
    public void GivenEmptyForEach_WhenEvaluated_ThenSkipsResource()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "name", Array.Empty<object>() }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    ForEach = "name",
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Empty(rows);
    }

    [Fact]
    public void GivenEmptyForEachOrNull_WhenEvaluated_ThenCreatesRowWithNull()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "name", Array.Empty<object>() }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                },
                new SelectGroup
                {
                    ForEachOrNull = "name",
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Null(rows[0]["family"]);
    }

    [Fact]
    public void GivenEmptyForEachOrNullWithNestedSelect_WhenEvaluated_ThenNullRowIncludesNestedColumns()
    {
        // forEachOrNull with a nested select — null row must include columns from nested selects
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "name", Array.Empty<object>() }
        };
        var resource = CreateTypedElement(patientJson);

        // Construct via JSON to express nested select structure not representable in SelectGroup model
        var json = """
            {
              "resource": "Patient",
              "select": [
                { "column": [{ "name": "id", "path": "id", "type": "id" }] },
                {
                  "forEachOrNull": "name",
                  "column": [{ "name": "family", "path": "family", "type": "string" }],
                  "select": [
                    { "column": [{ "name": "given", "path": "given", "type": "string" }] }
                  ]
                }
              ]
            }
            """;

        var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var sourceNode = JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");

        // Act
        var rows = _evaluator.Evaluate(sourceNode, resource).ToList();

        // Assert: one null row with all columns present (including nested "given")
        Assert.Single(rows);
        Assert.True(rows[0].ContainsKey("family"), "null row should include direct column 'family'");
        Assert.True(rows[0].ContainsKey("given"), "null row should include nested select column 'given'");
        Assert.Null(rows[0]["family"]);
    }

    [Fact]
    public void GivenRepeatWithNoMatchingPath_WhenEvaluated_ThenReturnsNoRows()
    {
        // Arrange: repeat path doesn't exist on the resource
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Repeat = ["contact"],
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "contact_id", Path = "id", Type = "string" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert: repeat with no matches yields no rows
        Assert.Empty(rows);
    }

    [Fact]
    public void GivenVariable_WhenEvaluated_ThenAccessibleAsFhirPathPercent()
    {
        // Arrange: constant declared in ViewDefinition with a default; variable overrides it at runtime.
        // %name references must always be declared as constants — variables override the value at evaluation time.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p1" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewJson = """
            {
              "resource": "Patient",
              "constant": [{ "name": "myTag", "valueString": "default" }],
              "select": [{
                "column": [
                  { "name": "id", "path": "id" },
                  { "name": "tag", "path": "%myTag" }
                ]
              }]
            }
            """;
        var jsonNode = JsonNode.Parse(viewJson)!;
        var sourceNode = JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");
        var variables = new Dictionary<string, string> { ["myTag"] = "hello" };

        // Act
        var rows = _evaluator.Evaluate(sourceNode, resource, variables).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("p1", rows[0]["id"]);
        Assert.Equal("hello", rows[0]["tag"]);
    }

    [Fact]
    public void GivenVariableWithSameNameAsConstant_WhenEvaluated_ThenVariableTakesPrecedence()
    {
        // Arrange: ViewDefinition declares constant "myTag" = "from-constant",
        // caller supplies variable "myTag" = "from-caller". Caller wins.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p2" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewJson = """
            {
              "resource": "Patient",
              "constant": [{ "name": "myTag", "valueString": "from-constant" }],
              "select": [{
                "column": [
                  { "name": "id", "path": "id" },
                  { "name": "tag", "path": "%myTag" }
                ]
              }]
            }
            """;
        var jsonNode = JsonNode.Parse(viewJson)!;
        var sourceNode = JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");
        var variables = new Dictionary<string, string> { ["myTag"] = "from-caller" };

        // Act
        var rows = _evaluator.Evaluate(sourceNode, resource, variables).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("from-caller", rows[0]["tag"]);
    }

    [Theory]
    [InlineData("valueDate", "2020-01-01", true)]
    [InlineData("valueDate", "1970-01-01", false)]
    [InlineData("valueDateTime", "2020-01-01T00:00:00Z", true)]
    [InlineData("valueInstant", "2020-01-01T00:00:00Z", true)]
    public void GivenATemporalConstant_WhenComparedAgainstAResourceDate_ThenItOrdersAsATemporal(
        string valueProperty, string constantValue, bool expectRow)
    {
        // The constant's declared type is the only place its temporal-ness is recorded: valueDate,
        // valueDateTime, valueInstant and valueTime all collapse to a bare string in the parser, so
        // without carrying the value[x] suffix through, %cutoff typed as System.String and this
        // conformant ViewDefinition threw "Cannot compare 'date' with 'string'" once the comparison
        // operators began rejecting unrelated operand types.
        //
        // The official suite is blind to this: its only comparison-against-a-constant row uses
        // valueDecimal, which survives the parser's own Decimal arm, and every temporal constant in the
        // suite is compared with = rather than an ordering operator.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p-temporal" },
            { "birthDate", "1980-06-15" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewJson = $$"""
            {
              "resource": "Patient",
              "constant": [{ "name": "cutoff", "{{valueProperty}}": "{{constantValue}}" }],
              "where": [{ "path": "birthDate < %cutoff" }],
              "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var rows = _evaluator.Evaluate(sourceNode, resource).ToList();

        Assert.Equal(expectRow ? 1 : 0, rows.Count);
    }

    [Fact]
    public void GivenATimeConstant_WhenComparedAgainstAResourceTime_ThenItOrdersAsATemporal()
    {
        // valueTime is the fourth temporal suffix and the only one the theory above cannot reach: a
        // time of day against a Date is a type error, not an ordering, so it needs a time-valued
        // element to compare with. Without it SystemTypeOf("Time") is exercised only by the official
        // suite's equality rows, and equality would pass just as happily with the "string" the parser
        // used to hand back - leaving the one mapping the fix exists for unvalidated for ordering.
        var observationJson = new Dictionary<string, object?>
        {
            { "resourceType", "Observation" },
            { "id", "o-time" },
            { "status", "final" },
            { "valueTime", "10:30:00" }
        };
        var resource = CreateTypedElement(observationJson);

        var viewJson = """
            {
              "resource": "Observation",
              "constant": [{ "name": "cutoff", "valueTime": "09:00:00" }],
              "where": [{ "path": "value.ofType(time) > %cutoff" }],
              "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var rows = _evaluator.Evaluate(sourceNode, resource).ToList();

        Assert.Single(rows);
    }

    [Fact]
    public void GivenAVariableOverridingATemporalConstant_WhenCompared_ThenItInheritsTheConstantsDeclaredType()
    {
        // Caller-supplied variables arrive as IReadOnlyDictionary<string, string>, which carries no type,
        // so a variable overriding a valueDate constant would otherwise retype the slot as System.String
        // and reintroduce the very failure the constant path was fixed for. The caller wins on the
        // value; the declared type belongs to the slot. This is also the supported workaround for the
        // limitation pinned below - declare the constant with the right value[x], override its value.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p-var" },
            { "birthDate", "1980-06-15" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewJson = """
            {
              "resource": "Patient",
              "constant": [{ "name": "cutoff", "valueDate": "1900-01-01" }],
              "where": [{ "path": "birthDate < %cutoff" }],
              "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        // The declared default excludes the patient; the caller's override includes them. A row proves
        // the override took effect, and that it was still ordered as a date rather than throwing.
        var withDefault = _evaluator.Evaluate(sourceNode, resource).ToList();
        var overridden = _evaluator
            .Evaluate(sourceNode, resource, new Dictionary<string, string> { ["cutoff"] = "2020-01-01" })
            .ToList();

        Assert.Empty(withDefault);
        Assert.Single(overridden);
    }

    [Fact]
    public void GivenAVariableWithNoMatchingConstant_WhenReferenced_ThenTheViewDefinitionIsRejected()
    {
        // The parser rejects any %name that is neither a declared constant nor one of the predefined
        // variables, before evaluation and regardless of what the caller passes.
        //
        // This test says nothing about the predefined names, and an earlier comment here wrongly
        // generalised from it to all names - claiming the untyped path was unreachable. "cutoff" is
        // simply not on the exemption list. The GivenAPredefinedVariableName_* tests below cover the
        // names that are, which is where the generalisation actually had to be checked.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p-var" },
            { "birthDate", "1980-06-15" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewJson = """
            {
              "resource": "Patient",
              "constant": [{ "name": "unrelated", "valueString": "x" }],
              "where": [{ "path": "birthDate < %cutoff" }],
              "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var thrown = Assert.Throws<InvalidOperationException>(
            () => _evaluator
                .Evaluate(sourceNode, resource, new Dictionary<string, string> { ["cutoff"] = "2020-01-01" })
                .ToList());

        var cause = Assert.IsType<InvalidOperationException>(thrown.InnerException);
        Assert.Contains("undefined constant '%cutoff'", cause.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ucum", "http://unitsofmeasure.org")]
    [InlineData("sct", "http://snomed.info/sct")]
    [InlineData("loinc", "http://loinc.org")]
    public void GivenAPredefinedVariableName_WhenOverriddenWithNoDeclaredConstant_ThenItBindsAsAString(
        string predefinedName, string defaultUri)
    {
        // The three names that genuinely reach the variable loop with no constant to inherit from.
        // ValidateConstantReferences exempts context, resource, rootResource, ucum, sct, loinc, rowIndex
        // and the vs- prefix - but of those only these three arrive here untyped: the first three are
        // answered by TryGetEnvironmentVariable's switch before it consults caller variables, rowIndex
        // is re-injected afterwards and wins, and %vs-x never parses as a variable reference at all.
        //
        // System.String is the right answer for them, not a gap. FHIRPath defines %ucum, %sct and
        // %loinc as fixed URIs, so a caller-supplied string is already correctly typed - and the engine
        // documents the override as a deliberate feature. The row below proves the override takes
        // effect and that the default is a string too, so nothing is being silently retyped.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p-predef" },
            { "birthDate", "1980-06-15" }
        };
        var resource = CreateTypedElement(patientJson);

        string View(string filter) => $$"""
            {
              "resource": "Patient",
              "constant": [{ "name": "unrelated", "valueString": "x" }],
              "where": [{ "path": "{{filter}}" }],
              "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;
        var defaultNode = JsonNodeSourceNode.Create(
            JsonNode.Parse(View($"%{predefinedName} = '{defaultUri}'"))!, "ViewDefinition");
        var overriddenNode = JsonNodeSourceNode.Create(
            JsonNode.Parse(View($"%{predefinedName} = 'OVERRIDDEN'"))!, "ViewDefinition");

        Assert.Single(_evaluator.Evaluate(defaultNode, resource).ToList());
        Assert.Single(_evaluator
            .Evaluate(overriddenNode, resource,
                new Dictionary<string, string> { [predefinedName] = "OVERRIDDEN" })
            .ToList());
    }

    [Fact]
    public void GivenAPredefinedVariableName_WhenOrderedAgainstADate_ThenTheStringTypeErrorIsCorrect()
    {
        // The case the previous round's comment wrongly claimed could not happen. It can, and the throw
        // is the right outcome rather than a defect to fix: %ucum is a URI slot, so putting a date in it
        // and ordering a date against it is a genuine type mismatch. Fixing this by re-inferring the
        // type from the value's shape is exactly the defect this branch removed.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p-predef" },
            { "birthDate", "1980-06-15" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewJson = """
            {
              "resource": "Patient",
              "constant": [{ "name": "unrelated", "valueString": "x" }],
              "where": [{ "path": "birthDate < %ucum" }],
              "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var thrown = Assert.Throws<FhirPathEvaluationException>(
            () => _evaluator
                .Evaluate(sourceNode, resource, new Dictionary<string, string> { ["ucum"] = "1990-01-01" })
                .ToList());

        var cause = Assert.IsType<FhirPathEvaluationException>(thrown.InnerException);
        Assert.Contains("Cannot compare 'date' with 'string'", cause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAPredefinedVariableNameDeclaredAsAConstant_WhenOverridden_ThenItInheritsTheDeclaredType()
    {
        // The escape hatch, and the reason the string-to-string signature does not need widening. A
        // caller who wants a typed value under a predefined name declares a constant of that name with
        // the right value[x]; the inheritance rule then types the override. Exempt from the
        // must-be-declared check is not the same as forbidden from being declared.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p-predef" },
            { "birthDate", "1980-06-15" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewJson = """
            {
              "resource": "Patient",
              "constant": [{ "name": "ucum", "valueDate": "1900-01-01" }],
              "where": [{ "path": "birthDate < %ucum" }],
              "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        // The declared default excludes the patient; the override includes them, ordered as a date.
        Assert.Empty(_evaluator.Evaluate(sourceNode, resource).ToList());
        Assert.Single(_evaluator
            .Evaluate(sourceNode, resource, new Dictionary<string, string> { ["ucum"] = "1990-01-01" })
            .ToList());
    }

    [Fact]
    public void GivenAStringConstant_WhenComparedAgainstAResourceDate_ThenItIsStillATypeError()
    {
        // The restraint on the row above. Carrying the declared type must not make every constant
        // temporal by shape: valueString stays System.String, so the comparison is between types
        // FHIRPath does not relate and the error the operators were changed to raise still fires.
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p-temporal" },
            { "birthDate", "1980-06-15" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewJson = """
            {
              "resource": "Patient",
              "constant": [{ "name": "cutoff", "valueString": "2020-01-01" }],
              "where": [{ "path": "birthDate < %cutoff" }],
              "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        // Asserted on the inner exception, not the outer. SqlOnFhirEvaluator.EvaluateBatch catches
        // FhirPathEvaluationException and rethrows the same type wrapping it, so the outer message is
        // only "Failed to evaluate ViewDefinition for resource type 'Patient'" - which a JSON parse
        // failure, a missing constant or a null-reference in the visitor would produce just as readily.
        // ThrowsAny<Exception> here asserted nothing about *why* it threw.
        var thrown = Assert.Throws<FhirPathEvaluationException>(
            () => _evaluator.Evaluate(sourceNode, resource).ToList());

        var cause = Assert.IsType<FhirPathEvaluationException>(thrown.InnerException);
        Assert.Contains("must be of the same type", cause.Message, StringComparison.Ordinal);
        Assert.Contains("'date'", cause.Message, StringComparison.Ordinal);
        Assert.Contains("'string'", cause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenNullVariables_WhenEvaluated_ThenNoRegression()
    {
        // Arrange: passing null variables should behave the same as omitting them
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p3" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource, null).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("p3", rows[0]["id"]);
    }

    [Fact]
    public void GivenSiblingSelectAndUnionAllAcrossResources_WhenBatchEvaluated_ThenEachRowKeepsItsOwnResourceColumns()
    {
        // Regression: batch UNION ALL ordering must evaluate sibling selects against each
        // row's originating resource (not resources[0]) and order branch-major across resources.
        var pt1 = CreateTypedElement(new Dictionary<string, object?> { { "resourceType", "Patient" }, { "id", "pt1" } });
        var pt2 = CreateTypedElement(new Dictionary<string, object?> { { "resourceType", "Patient" }, { "id", "pt2" } });

        var viewJson = """
            {
              "resource": "Patient",
              "select": [
                { "column": [{ "name": "id", "path": "id", "type": "id" }] },
                {
                  "unionAll": [
                    { "column": [{ "name": "source", "path": "'a'", "type": "string" }] },
                    { "column": [{ "name": "source", "path": "'b'", "type": "string" }] }
                  ]
                }
              ]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var rows = _evaluator.EvaluateBatch(sourceNode, [pt1, pt2]).ToList();

        Assert.Equal(4, rows.Count);
        Assert.Equal(_batchUnionAllExpectedIds, rows.Select(r => r["id"]?.ToString()).ToArray());
        Assert.Equal(_batchUnionAllExpectedSources, rows.Select(r => r["source"]?.ToString()).ToArray());
    }

    [Fact]
    public void GivenWhereClauseAndUnionAllAcrossResources_WhenBatchEvaluated_ThenWhereFiltersPerResourceInEveryBranch()
    {
        // The batch ordering rebuilds a single-branch sub-view per branch; the top-level WHERE
        // must survive that rebuild and filter each resource in every branch. pt2 is inactive and
        // must contribute zero rows to both branch 'a' and branch 'b'.
        var pt1 = CreateTypedElement(new Dictionary<string, object?> { { "resourceType", "Patient" }, { "id", "pt1" }, { "active", true } });
        var pt2 = CreateTypedElement(new Dictionary<string, object?> { { "resourceType", "Patient" }, { "id", "pt2" }, { "active", false } });

        var viewJson = """
            {
              "resource": "Patient",
              "where": [{ "path": "active = true" }],
              "select": [
                { "column": [{ "name": "id", "path": "id", "type": "id" }] },
                {
                  "unionAll": [
                    { "column": [{ "name": "source", "path": "'a'", "type": "string" }] },
                    { "column": [{ "name": "source", "path": "'b'", "type": "string" }] }
                  ]
                }
              ]
            }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var rows = _evaluator.EvaluateBatch(sourceNode, [pt1, pt2]).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(_batchWhereExpectedIds, rows.Select(r => r["id"]?.ToString()).ToArray());
        Assert.Equal(_batchWhereExpectedSources, rows.Select(r => r["source"]?.ToString()).ToArray());
    }

    [Fact]
    public void GivenNoResources_WhenBatchEvaluated_ThenReturnsEmptyNotNull()
    {
        var viewJson = """
            { "resource": "Patient", "select": [{ "column": [{ "name": "id", "path": "id", "type": "id" }] }] }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var rows = _evaluator.EvaluateBatch(sourceNode, Array.Empty<IElement>()).ToList();

        Assert.Empty(rows);
    }

    [Fact]
    public void GivenEvaluationFailureOnFallbackPath_WhenBatchEvaluated_ThenWrapsInInvalidOperationExceptionWithContext()
    {
        // The non-unionAll path returns a fallback over per-resource Evaluate. It must be eager so
        // failures surface inside the evaluator's try/catch and are wrapped with resource-type context,
        // not thrown raw at enumeration time. A collection=false column over a multi-valued path throws.
        var patient = CreateTypedElement(new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p1" },
            { "name", new object[] { new Dictionary<string, object?> { { "given", _multiGivenNames } } } }
        });

        var viewJson = """
            { "resource": "Patient", "select": [{ "column": [{ "name": "given", "path": "name.given", "type": "string" }] }] }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var ex = Assert.Throws<InvalidOperationException>(() => _evaluator.EvaluateBatch(sourceNode, [patient]).ToList());
        Assert.Contains("Failed to evaluate ViewDefinition for resource type 'Patient'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAFhirPathEvaluationError_WhenBatchEvaluated_ThenTheErrorTypeSurvivesTheContextWrapping()
    {
        // A spec-mandated FHIRPath error - '&' is a singleton operator given a three-item left operand -
        // must stay distinguishable from an engine defect after the evaluator adds resource-type context.
        // Wrapping every failure in a bare InvalidOperationException erased that distinction before any
        // ViewDefinition consumer could act on it.
        var patient = CreateTypedElement(new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "p1" }
        });

        var viewJson = """
            { "resource": "Patient", "select": [{ "column": [{ "name": "bad", "path": "(1 | 2 | 3) & 'b'", "type": "string" }] }] }
            """;
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        var ex = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.EvaluateBatch(sourceNode, [patient]).ToList());

        // The message and inner exception keep the shape callers already depend on.
        Assert.Contains("Failed to evaluate ViewDefinition for resource type 'Patient'", ex.Message, StringComparison.Ordinal);
        Assert.IsType<FhirPathEvaluationException>(ex.InnerException);

        // And it is still an InvalidOperationException, so existing catch sites are unaffected.
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    private static IElement CreateTypedElement(Dictionary<string, object?> data)
    {
        // Use real ResourceJsonNode instead of mocks for proper FHIR semantics
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        var resourceNode = ResourceJsonNode.Parse(json);
        return (IElement)resourceNode.ToElement(_schemaProvider);
    }

    private static ISourceNavigator ConvertToSourceNode(ViewDefinition viewDef)
    {
        // Convert ViewDefinition model to JSON and then to ISourceNavigator
        // Use camelCase naming policy to match FHIR JSON conventions
        var json = JsonSerializer.Serialize(viewDef, _jsonOptions);
        var jsonNode = JsonNode.Parse(json)!;
        return JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");
    }
}
